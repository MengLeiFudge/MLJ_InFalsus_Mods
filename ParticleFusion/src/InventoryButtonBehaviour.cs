using GameInputManager = ifapp.Game.Input.InputManager;
using Il2CppInterop.Runtime.Attributes;
using ifapp.Game.Scenes.Recipe;
using ifapp.Game.UI.Common;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace InFalsusMod;

/// <summary>随库存界面维护原版融合入口，并通过游戏 Modal 完成确认、执行和结果反馈。</summary>
public sealed class InventoryButtonBehaviour : MonoBehaviour
{
    /// <summary>每个库存实例拥有自己的按钮；创建失败也保留记录，避免逐帧重试和刷日志。</summary>
    private readonly List<ButtonBinding> bindings = new();
    /// <summary>智能确认窗口中的原版减量与增量按钮。</summary>
    private readonly List<UIButton> limitButtons = new();
    /// <summary>下一次查找新库存界面的非缩放时间，单位为秒。</summary>
    private float nextDiscovery;
    /// <summary>当前由本组件占用的原版 Modal 会话。</summary>
    private NativeModalSession? modalSession;
    /// <summary>确认窗口所属的粒子界面。</summary>
    private IotaInventoryLayer? dialogOwner;
    /// <summary>玩家尚未确认或等待执行的只读数量预览。</summary>
    private FusionPreview? preview;
    /// <summary>与 <see cref="preview"/> 同一快照的融合预估，调整材料上限时直接查表。</summary>
    private FusionForecast? forecast;
    /// <summary>玩家确认后置位，下一帧 Update 只执行一次。</summary>
    private bool executeRequested;
    /// <summary>发起确认的帧编号，防止原版按钮回调所在帧立即执行。</summary>
    private int requestedFrame;
    /// <summary>关闭原版窗口当帧不接受库存入口的同一鼠标释放。</summary>
    private int releaseInputFrame = -1;
    /// <summary>窗口打开时的供体模式，结果阶段也保留正确标题。</summary>
    private bool selectedMode;
    /// <summary>玩家设置的不同供体颗数上限，合法范围为 0..MaximumDecomposed。</summary>
    private int donorLimit;
    /// <summary>等待原版 Modal 空闲后显示的结果或错误。</summary>
    private UiMessage? pendingMessage;
    /// <summary>待显示消息对应的本地化标题键。</summary>
    private string pendingTitleKey = "SmartTitle";

    /// <summary>绑定由 IL2CPP 创建的组件实例。</summary>
    /// <param name="pointer">Unity 组件的原生对象指针。</param>
    public InventoryButtonBehaviour(IntPtr pointer) : base(pointer) { }

    /// <summary>发现库存界面、观察原版窗口生命周期，并执行已确认的融合。</summary>
    public void Update()
    {
        ObserveModalClosure();
        HandleLimitKeyboard();
        ExecuteApprovedFusion();
        TryShowPendingMessage();
        UpdateEntryButtons();
        DiscoverInventories();
    }

    /// <summary>原版 ESC、关闭按钮或回调结束窗口后，释放附加控件和本次预览。</summary>
    private void ObserveModalClosure()
    {
        if (modalSession == null || modalSession.IsOpen)
            return;

        var closed = modalSession;
        modalSession = null;
        if (closed.Failure != null)
            Plugin.Logger.LogWarning($"原版融合窗口状态读取失败，按已关闭清理：{closed.Failure}");
        DestroyLimitButtons();
        releaseInputFrame = Time.frameCount;
        if (!executeRequested)
        {
            preview = null;
            forecast = null;
            dialogOwner = null;
        }
    }

    /// <summary>智能确认保持打开时，左右方向键以一颗为单位调整供体上限。</summary>
    private void HandleLimitKeyboard()
    {
        if (!CanSetLimit)
            return;
        if (Keyboard.current?.leftArrowKey.wasPressedThisFrame == true)
            AdjustDonorLimit(-1);
        if (Keyboard.current?.rightArrowKey.wasPressedThisFrame == true)
            AdjustDonorLimit(1);
    }

    /// <summary>确认帧之后重新核对库存并提交；原版 Modal 回调本身不修改存档。</summary>
    private void ExecuteApprovedFusion()
    {
        if (!executeRequested || Time.frameCount <= requestedFrame || modalSession?.IsOpen == true)
            return;

        executeRequested = false;
        var approved = preview;
        var owner = dialogOwner;
        preview = null;
        forecast = null;
        dialogOwner = null;
        try
        {
            if (approved == null || owner == null || !owner.isActiveAndEnabled
                || GameInputManager.Instance == null || !GameInputManager.Instance.IsHeadOfStack(owner))
                throw new InvalidOperationException("粒子界面已经切换，未执行融合。");
            QueueMessage(InventoryEditor.Apply(approved!, owner!, donorLimit));
        }
        catch (Exception ex)
        {
            QueueMessage(new UiMessage("Failed", ex));
            Plugin.Logger.LogError(ex);
        }
    }

    /// <summary>原版窗口被其他游戏流程占用时保留消息，空闲后再显示且不抢占现有内容。</summary>
    private void TryShowPendingMessage()
    {
        if (pendingMessage == null || modalSession != null)
            return;
        try
        {
            if (!NativeModal.TryShowMessage(ModText.Get(pendingTitleKey), pendingMessage.ToString(), out var session))
                return;
            modalSession = session;
            pendingMessage = null;
        }
        catch (Exception ex)
        {
            pendingMessage = null;
            Plugin.Logger.LogError($"原版融合结果窗口创建失败：{ex}");
        }
    }

    /// <summary>更新所有库存实例的入口可见性、原版状态、文本和布局。</summary>
    private void UpdateEntryButtons()
    {
        for (int i = bindings.Count - 1; i >= 0; i--)
        {
            var binding = bindings[i];
            if (binding.Owner == null || binding.Template == null)
            {
                if (binding.Button != null)
                    Object.Destroy(binding.Button.gameObject);
                bindings.RemoveAt(i);
                continue;
            }
            if (binding.Button == null)
                continue;

            bool visible = binding.Owner.isActiveAndEnabled
                && !binding.Owner.DisableInputEvents
                && binding.Template.gameObject.activeInHierarchy;
            var root = binding.Button.gameObject;
            if (root.activeSelf != visible)
                root.SetActive(visible);
            if (!visible)
                continue;

            bool enabled = !binding.Owner._fBA || (binding.Owner._OcA?.Count ?? 0) > 0;
            if (binding.Enabled != enabled)
            {
                NativeButtons.SetState(binding.Button, enabled);
                binding.Enabled = enabled;
            }
            string key = binding.Owner._fBA ? "SelectedButton" : "SmartButton";
            if (binding.Language != ModText.Language || binding.TextKey != key)
            {
                NativeButtons.UpdateText(binding.Button, binding.Template, ModText.Get(key));
                binding.Language = ModText.Language;
                binding.TextKey = key;
            }
            var layout = binding.Template.constrained2D.constrained2DTransform;
            // Constrained2D 的 offset 使用设计像素，Y 正方向向上；间距为原按钮高度的四分之一。
            binding.Button.constrained2D._sQA(
                new Vector2(layout.offset.x, layout.offset.y + layout.sizeInPixels.y * 1.25f));
        }
    }

    /// <summary>以固定间隔发现新的原版库存实例，并为每个实例建立独立入口。</summary>
    private void DiscoverInventories()
    {
        if (Time.realtimeSinceStartup < nextDiscovery)
            return;
        nextDiscovery = Time.realtimeSinceStartup + 0.5f;

        foreach (var owner in Object.FindObjectsOfType<IotaInventoryLayer>())
        {
            if (!owner.isActiveAndEnabled || owner.selectMultipleButton == null
                || bindings.Any(binding => binding.Owner == owner))
                continue;

            var binding = new ButtonBinding(owner, owner.selectMultipleButton);
            bindings.Add(binding);
            try
            {
                binding.Button = CreateButton(binding.Template, owner);
                Plugin.Logger.LogDebug("已建立粒子融合入口。");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"粒子融合按钮创建失败：{ex}");
            }
        }
    }

    /// <summary>建立有原版输入归属的融合按钮，并按库存设计尺寸定位。</summary>
    /// <param name="template">当前库存的批量选择按钮。</param>
    /// <param name="owner">当前库存界面。</param>
    /// <returns>完成配置的融合按钮。</returns>
    private UIButton CreateButton(UIButton template, IotaInventoryLayer owner)
    {
        if (template.constrained2D == null
            || template.constrained2D.constrained2DTransform.sizeInPixels.y <= 0)
            throw new InvalidOperationException("批量选择按钮的布局不兼容，无法定位融合按钮。");
        var button = NativeButtons.Create(template, owner, "ParticleFusion.Button",
            ModText.Get(owner._fBA ? "SelectedButton" : "SmartButton"), OpenPreview);
        try
        {
            var layout = template.constrained2D.constrained2DTransform;
            button.constrained2D._sQA(
                new Vector2(layout.offset.x, layout.offset.y + layout.sizeInPixels.y * 1.25f));
            return button;
        }
        catch
        {
            Object.Destroy(button.gameObject);
            throw;
        }
    }

    /// <summary>点击只生成只读预览并尝试占用空闲原版 Modal；不在入口回调中修改库存。</summary>
    /// <param name="button">触发预览的融合按钮。</param>
    private void OpenPreview(UIButton button)
    {
        var binding = bindings.FirstOrDefault(item => item.Button == button);
        if (modalSession != null || pendingMessage != null || executeRequested
            || Time.frameCount == releaseInputFrame || binding == null || binding.Owner == null
            || !binding.Owner.isActiveAndEnabled || binding.Owner.DisableInputEvents
            || !button.gameObject.activeInHierarchy
            || (binding.Owner._fBA && (binding.Owner._OcA?.Count ?? 0) == 0))
            return;

        dialogOwner = binding.Owner;
        selectedMode = binding.Owner._fBA;
        try
        {
            preview = InventoryEditor.Preview(binding.Owner);
            if (preview.Selection.SelectedMode && preview.Selection.Count == 0)
                throw new InvalidOperationException("库存或选择已经变化，请重新查看数量并确认。");
            donorLimit = preview.MaximumDecomposed;
            forecast = InventoryEditor.Forecast(preview);
            if (!NativeModal.TryShowConfirmation(ModText.Get(TitleKey), ConfirmationBody(),
                    CancelPreview, ConfirmPreview, out var session))
            {
                preview = null;
                forecast = null;
                dialogOwner = null;
                Plugin.Logger.LogDebug("原版 Modal 正忙，未打开融合预览。");
                return;
            }
            modalSession = session;
            if (!selectedMode)
                CreateLimitButtons();
            Plugin.Logger.LogDebug($"融合预览：模式={(selectedMode ? "选中供体" : "智能")}，库存{preview.Total}颗，供体上限{donorLimit}颗。");
        }
        catch (Exception ex)
        {
            modalSession?.Close();
            DestroyLimitButtons();
            preview = null;
            forecast = null;
            dialogOwner = null;
            QueueMessage(new UiMessage("Unavailable", ex));
            Plugin.Logger.LogWarning($"融合预览失败：{ex}");
        }
    }

    /// <summary>取消回调只释放预览；原版适配器已先执行 Modal 关闭流程。</summary>
    private void CancelPreview()
    {
        preview = null;
        forecast = null;
        dialogOwner = null;
        executeRequested = false;
        releaseInputFrame = Time.frameCount;
        DestroyLimitButtons();
        Plugin.Logger.LogDebug("融合预览已取消。");
    }

    /// <summary>确认回调登记下一帧执行，保持预览和库存 owner 供重新核对。</summary>
    private void ConfirmPreview()
    {
        executeRequested = true;
        requestedFrame = Time.frameCount;
        releaseInputFrame = Time.frameCount;
        DestroyLimitButtons();
    }

    /// <summary>在智能确认框的原版主按钮上方创建减量和增量步进按钮。</summary>
    private void CreateLimitButtons()
    {
        var session = modalSession ?? throw new InvalidOperationException("原版融合确认窗口尚未建立。");
        var modal = session.Modal;
        var template = modal.leftButton ?? throw new InvalidOperationException("原版 Modal 缺少主按钮模板。");
        var parent = template.transform.parent;
        var left = modal.leftButtonC2d.constrained2DTransform;
        var right = modal.rightButtonC2d.constrained2DTransform;
        float height = Math.Max(36f, Math.Min(left.sizeInPixels.y, right.sizeInPixels.y));
        float centerX = (left.offset.x + right.offset.x) * 0.5f;
        float y = Math.Max(left.offset.y, right.offset.y) + height * 1.25f;
        var size = new Vector2(Math.Max(56f, height), height);

        var decrease = NativeButtons.Create(template, modal, "ParticleFusion.DonorDecrease", "-",
            _ => AdjustDonorLimit(-LimitButtonStep));
        var increase = NativeButtons.Create(template, modal, "ParticleFusion.DonorIncrease", "+",
            _ => AdjustDonorLimit(LimitButtonStep));
        limitButtons.Add(decrease);
        limitButtons.Add(increase);
        NativeButtons.Place(decrease, parent, new Vector2(centerX - size.x * 0.65f, y), size);
        NativeButtons.Place(increase, parent, new Vector2(centerX + size.x * 0.65f, y), size);
        UpdateLimitButtons();
    }

    /// <summary>按指定增量调整供体上限，并立即更新原版正文和按钮边界状态。</summary>
    /// <param name="delta">供体颗数变化；负值减少，正值增加。</param>
    private void AdjustDonorLimit(int delta)
    {
        if (!CanSetLimit || delta == 0)
            return;
        int next = Math.Clamp(donorLimit + delta, 0, preview!.MaximumDecomposed);
        if (next == donorLimit)
            return;
        donorLimit = next;
        modalSession!.UpdateBody(ConfirmationBody());
        UpdateLimitButtons();
    }

    /// <summary>根据供体上限边界同步两个原版步进按钮的可用状态。</summary>
    private void UpdateLimitButtons()
    {
        if (limitButtons.Count != 2 || preview == null)
            return;
        NativeButtons.SetState(limitButtons[0], donorLimit > 0);
        NativeButtons.SetState(limitButtons[1], donorLimit < preview.MaximumDecomposed);
    }

    /// <summary>销毁仅属于当前 Modal 会话的步进按钮，避免后续游戏窗口继承模组控件。</summary>
    private void DestroyLimitButtons()
    {
        foreach (var button in limitButtons)
            if (button != null)
                Object.Destroy(button.gameObject);
        limitButtons.Clear();
    }

    /// <summary>生成当前语言的完整预览正文；智能模式追加实时供体上限，两种模式都附当前上限下的预估结果。</summary>
    /// <returns>可直接交给原版文本组件的正文。</returns>
    private string ConfirmationBody()
    {
        var current = preview ?? throw new InvalidOperationException("融合预览已经失效。");
        var expected = (forecast ?? throw new InvalidOperationException("融合预览已经失效。")).At(donorLimit);
        string body = new UiMessage(selectedMode ? "SelectedPreview" : "SmartPreview",
            current.Total, current.Selection.Count).ToString();
        body += "\n\n";
        if (!selectedMode)
            body += ModText.Get("DonorLimit", donorLimit) + "\n";
        return body + ModText.Get("Estimate", expected.Decomposed, expected.Strengthened);
    }

    /// <summary>保存一条结果或错误，并沿用发起操作时的模式标题。</summary>
    /// <param name="message">等待原版窗口显示的本地化消息。</param>
    [HideFromIl2Cpp]
    private void QueueMessage(UiMessage message)
    {
        pendingMessage = message;
        pendingTitleKey = TitleKey;
    }

    /// <summary>当前融合模式对应的标题资源键。</summary>
    private string TitleKey => selectedMode ? "SelectedTitle" : "SmartTitle";

    /// <summary>仅智能预览且原版确认窗口仍打开时允许调整供体上限。</summary>
    private bool CanSetLimit => preview != null && !preview.Selection.SelectedMode
        && !executeRequested && modalSession?.IsOpen == true;

    /// <summary>鼠标步进按钮每次调整约 5% 可用供体，至少为一颗；方向键始终逐颗调整。</summary>
    private int LimitButtonStep => preview == null ? 1 : Math.Max(1, preview.MaximumDecomposed / 20);

    /// <summary>组件卸载时关闭自己的原版会话，并销毁本组件创建的所有按钮。</summary>
    public void OnDestroy()
    {
        executeRequested = false;
        preview = null;
        forecast = null;
        pendingMessage = null;
        var closingSession = modalSession;
        closingSession?.Close();
        if (closingSession?.Failure != null)
            Plugin.Logger.LogWarning($"组件卸载时关闭原版融合窗口失败：{closingSession.Failure}");
        modalSession = null;
        DestroyLimitButtons();
        foreach (var binding in bindings)
            if (binding.Button != null)
                Object.Destroy(binding.Button.gameObject);
        bindings.Clear();
    }

    /// <summary>记录库存、定位基准及其专属按钮，生命周期跟随库存实例。</summary>
    private sealed class ButtonBinding
    {
        /// <summary>控制按钮可见性和数据操作时机的库存界面。</summary>
        internal readonly IotaInventoryLayer Owner;
        /// <summary>用于复制外观并定位的原版批量选择按钮。</summary>
        internal readonly UIButton Template;
        /// <summary>新建按钮；创建失败时为空，不在同一库存实例上重复创建。</summary>
        internal UIButton? Button;
        /// <summary>上次更新按钮文案的语言；字体语言另与模板核对。</summary>
        internal Str.Strings.Localization Language;
        /// <summary>入口当前模式的文案键。</summary>
        internal string? TextKey;
        /// <summary>缓存启用状态，避免重复重置原版动画。</summary>
        internal bool? Enabled;

        /// <summary>建立当前库存实例与原版按钮之间的归属。</summary>
        /// <param name="owner">按钮所属的库存界面。</param>
        /// <param name="template">原版批量选择按钮。</param>
        internal ButtonBinding(IotaInventoryLayer owner, UIButton template)
        {
            Owner = owner;
            Template = template;
        }
    }
}
