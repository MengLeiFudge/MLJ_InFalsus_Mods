using System.Globalization;
using ifapp.Game.Input;
using ifapp.Game.Scenes;
using UnityEngine;
using Key = UnityEngine.InputSystem.Key;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>全部最佳成绩页面的会话，拥有原生输入、视图及封面；导出仅响应玩家点击。</summary>
internal sealed class PotentialBrowser : IDisposable
{
    /// <summary>当前原生场景、排名快照及共享徽章。</summary>
    private readonly PotentialBadges badges;
    private SongSelectScene? owner;
    private PotentialSnapshot? snapshot, exporting, filteredSnapshot;
    private PotentialInputReceiver? receiver;
    private NativePotentialPage? view;
    private CoverCache? covers;
    /// <summary>搜索内容和 Unicode 文本选区；筛选后仍保留原始排名。</summary>
    private string search = "", filteredSearch = "";
    private int caret, anchor;
    private bool focused, closeRequested;
    /// <summary>入口释放后再打开；封面导出等待有明确的时间上限。</summary>
    private int requestedFrame = -1;
    private float exportStarted;
    private string status = "";

    internal bool IsOpen => receiver != null;

    internal PotentialBrowser(PotentialBadges badges) => this.badges = badges;

    /// <summary>只在选歌场景拥有输入时预约打开，防止场景切换期间抢占输入。</summary>
    internal void RequestOpen(SongSelectScene scene, PotentialSnapshot? data)
    {
        if (IsOpen || requestedFrame >= 0 || data == null || InputManager.Instance?.IsHeadOfStack(scene) != true)
            return;
        owner = scene;
        snapshot = data;
        requestedFrame = Time.frameCount;
    }

    /// <summary>在同一异常边界内管理原生视图、过滤结果与异步导出。</summary>
    internal void Update(SongSelectScene? scene, PotentialSnapshot? data)
    {
        if (closeRequested || owner == null || owner != scene || !owner.isActiveAndEnabled || data == null)
        { Close(); return; }
        snapshot = data;
        if (requestedFrame >= 0 && Time.frameCount > requestedFrame)
        {
            requestedFrame = -1;
            if (InputManager.Instance?.IsHeadOfStack(owner) != true)
                return;
            var root = new GameObject("InFalsus.PotentialSystem.BrowserInput");
            try
            {
                root.layer = owner.gameObject.layer;
                root.transform.SetParent(owner.transform, false);
                receiver = root.AddComponent<PotentialInputReceiver>();
                receiver.Bind(this);
                covers = new CoverCache();
                search = "";
                caret = anchor = 0;
                focused = false;
                filteredSnapshot = null;
                status = "";
                view = new NativePotentialPage(owner, receiver, () => closeRequested = true,
                    () => Guard(RequestExport), () => { focused = true; caret = anchor = search.Length; });
                InputManager.Instance.TakeInputControl(receiver);
                InputManager.Instance.RequestLastMouseMoveReplay();
            }
            catch { Close(); if (root != null) Object.Destroy(root); throw; }
        }
        if (!IsOpen)
            return;
        if (InputManager.Instance?.IsHeadOfStack(receiver) != true)
        { Close(); return; }
        if (filteredSnapshot != snapshot || filteredSearch != search)
        {
            string query = search.Trim();
            var filtered = snapshot.Plays.Where(play => string.IsNullOrEmpty(query)
                || (play.Chart?.Title ?? play.Play.ChartId).Contains(query, StringComparison.OrdinalIgnoreCase)
                || (play.Chart?.Artist ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
                || PotentialPresentation.Difficulty(play.Play.Difficulty).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            view!.SetPlays(filtered);
            filteredSnapshot = snapshot;
            filteredSearch = search;
        }
        UpdateExport();
        string displayed = focused ? search.Insert(caret, "|") : search;
        view!.Render(snapshot, covers!, displayed, status, focused, exporting != null);
    }

    /// <summary>冻结点击时的 B50 快照；搜索和滚动不会改变导出内容。</summary>
    private void RequestExport()
    {
        if (!IsOpen || snapshot == null || exporting != null)
            return;
        focused = false;
        exporting = snapshot;
        exportStarted = Time.realtimeSinceStartup;
        status = "正在准备 B50 封面……";
    }

    /// <summary>等待异步封面并生成独立图片；只有剪贴板交付成功后提示已复制。</summary>
    private void UpdateExport()
    {
        if (exporting == null)
            return;
        try
        {
            bool ready = covers!.Prepare(exporting.B50, out var images, out var error);
            if (error != null)
                throw new InvalidOperationException("封面加载失败，未导出：" + error);
            if (!ready)
            {
                status = $"正在准备 B50 封面：{images.Count}/{exporting.B50.Count}，关闭页面可取消。";
                if (Time.realtimeSinceStartup - exportStarted > 40f)
                    throw new TimeoutException("封面加载超时，请稍后再次导出。");
                return;
            }
            var badge = badges.Pixels(PotentialBadges.Tier(exporting.Potential));
            ClipboardImage.Copy(B50Image.Render(exporting, images, badge));
            status = $"B50 图片已复制到剪贴板（{exporting.B50.Count} 项），可直接粘贴。";
            exporting = null;
        }
        catch (Exception ex)
        {
            exporting = null;
            status = "导出失败：" + ex.Message;
            Plugin.Logger.LogError($"B50 图片导出失败：{ex}");
        }
    }

    /// <summary>原生输入回调的异常不逃回游戏输入循环，下一次更新统一关闭页面。</summary>
    private void Guard(Action action)
    {
        try { if (IsOpen && !closeRequested) action(); }
        catch (Exception ex)
        {
            closeRequested = true;
            Plugin.Logger.LogError($"潜力值成绩页输入失败，已请求关闭：{ex}");
        }
    }

    /// <summary>原生滚轮只移动本页成绩列表。</summary>
    internal void Wheel(Vector2 delta) => Guard(() => view?.Wheel(delta));

    /// <summary>按原生字符事件输入，包括输入法已经提交的中文。</summary>
    internal void Text(char character) => Guard(() =>
    {
        if (focused && !char.IsControl(character))
            ReplaceSelection(character.ToString());
    });

    /// <summary>原生键盘负责退出与搜索编辑，选歌和开始快捷键保持被本页隔离。</summary>
    internal void Key(Key key, bool control, bool shift) => Guard(() =>
    {
        if (key == UnityEngine.InputSystem.Key.Escape)
        { closeRequested = true; return; }
        if (key == UnityEngine.InputSystem.Key.Tab)
        { focused = !focused; return; }
        if (!focused)
            return;
        int first = Math.Min(caret, anchor), last = Math.Max(caret, anchor);
        if (control)
        {
            switch (key)
            {
                case UnityEngine.InputSystem.Key.A: anchor = 0; caret = search.Length; return;
                case UnityEngine.InputSystem.Key.C:
                case UnityEngine.InputSystem.Key.X:
                    if (first != last)
                    {
                        GUIUtility.systemCopyBuffer = search[first..last];
                        if (key == UnityEngine.InputSystem.Key.X) ReplaceSelection("");
                    }
                    return;
                case UnityEngine.InputSystem.Key.V:
                    ReplaceSelection(new string((GUIUtility.systemCopyBuffer ?? "").Where(c => !char.IsControl(c)).ToArray()));
                    return;
            }
        }
        switch (key)
        {
            case UnityEngine.InputSystem.Key.Backspace:
                if (first == last) anchor = Previous(caret);
                ReplaceSelection(""); break;
            case UnityEngine.InputSystem.Key.Delete:
                if (first == last) anchor = Next(caret);
                ReplaceSelection(""); break;
            case UnityEngine.InputSystem.Key.LeftArrow:
                caret = !shift && first != last ? first : Previous(caret); break;
            case UnityEngine.InputSystem.Key.RightArrow:
                caret = !shift && first != last ? last : Next(caret); break;
            case UnityEngine.InputSystem.Key.Home: caret = 0; break;
            case UnityEngine.InputSystem.Key.End: caret = search.Length; break;
            case UnityEngine.InputSystem.Key.Enter:
            case UnityEngine.InputSystem.Key.NumpadEnter: focused = false; break;
        }
        if (!shift) anchor = caret;
    });

    /// <summary>以完整文本元素移动，避免退格或方向键拆开代理对和组合字符。</summary>
    private int Previous(int position) => StringInfo.ParseCombiningCharacters(search).LastOrDefault(i => i < position);
    private int Next(int position) => StringInfo.ParseCombiningCharacters(search).FirstOrDefault(i => i > position, search.Length);

    /// <summary>替换当前选区，限制原生搜索标签长度；完整歌名仍参与过滤和导出。</summary>
    private void ReplaceSelection(string text)
    {
        int first = Math.Min(caret, anchor), last = Math.Max(caret, anchor);
        int capacity = Math.Max(0, 256 - (search.Length - last + first));
        if (text.Length > capacity)
        {
            int boundary = StringInfo.ParseCombiningCharacters(text).LastOrDefault(i => i <= capacity);
            text = text[..boundary];
        }
        search = search[..first] + text + search[last..];
        caret = anchor = first + text.Length;
    }

    /// <summary>同步注销本页控件，然后归还输入；关闭不改原生成绩和剪贴板。</summary>
    internal void Close()
    {
        requestedFrame = -1;
        closeRequested = false;
        focused = false;
        exporting = null;
        owner = null;
        snapshot = filteredSnapshot = null;
        var current = receiver;
        receiver = null;
        var page = view;
        view = null;
        var resources = covers;
        covers = null;
        try { page?.Dispose(); }
        finally
        {
            try
            {
                if (current is not null)
                {
                    current.Unbind();
                    try { InputManager.Instance?.ReleaseInputControl(current); }
                    catch (Exception ex) { Plugin.Logger.LogError($"成绩页归还原版输入失败：{ex}"); }
                    finally { if (current != null) Object.Destroy(current.gameObject); }
                }
            }
            finally { resources?.Dispose(); }
        }
    }

    public void Dispose() => Close();
}
