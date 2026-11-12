using System.Reflection;
using HarmonyLib;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using ifapp.Game.Scenes;
using ifapp.Game.Scenes.Recipe;
using ifapp.Game.UI.Common;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>从静默加载的卡牌资源复制显示层级；选歌场景及其音乐始终保留，源场景不初始化业务。</summary>
internal sealed class PotentialPageCopy : IDisposable
{
    internal const string RootName = "InFalsus.PotentialSystem.PageCopy";
    private static PotentialPageCopy? active;
    internal static bool IsBusy => active != null;
    private AsyncOperationHandle<SceneInstance>? loading, unloading;
    /// <summary>在激活前记录源场景句柄，隔离不依赖 Addressables 地址键或打包后的场景名称。</summary>
    private int? sourceSceneHandle;
    private AsyncOperation? activation;
    private readonly SongSelectScene owner;
    private readonly List<Renderer> hidden = new();
    private bool disposed, releaseFailed;
    internal GameObject? Root { get; private set; }
    internal RecipeScene? Scene { get; private set; }

    /// <summary>独立加载模板，不经过 CoreScene 导航；同一时间只保留一个源场景引用。</summary>
    internal PotentialPageCopy(SongSelectScene owner)
    {
        if (active != null || Object.FindObjectsOfType<RecipeScene>(true).Length != 0)
            throw new InvalidOperationException("卡牌页面资源正在使用或释放，请稍后再打开。");
        this.owner = owner;
        active = this;
        try
        {
            loading = Addressables.LoadSceneAsync((Il2CppSystem.String)"RecipeScene", LoadSceneMode.Additive, activateOnLoad: false);
        }
        catch { active = null; throw; }
    }

    /// <summary>加载完成后将所有根节点一起复制，使跨根节点的序列化引用也指向副本。</summary>
    internal bool Prepare()
    {
        if (disposed) return false;
        if (Scene != null) return true;
        if (loading == null || !loading.IsDone) return false;
        if (loading.Status != AsyncOperationStatus.Succeeded)
            throw new InvalidOperationException("加载卡牌页面素材失败：" + loading.OperationException?.Message);
        if (!ActivateSource()) return false;
        var source = loading.Result.Scene;
        var roots = source.GetRootGameObjects();
        var group = new GameObject("InFalsus.PotentialSystem.PageSource");
        group.SetActive(false);
        SceneManager.MoveGameObjectToScene(group, source);
        foreach (var root in roots)
        {
            root.SetActive(false);
            root.transform.SetParent(group.transform, true);
        }
        Root = Object.Instantiate(group);
        Root.name = RootName;
        SceneManager.MoveGameObjectToScene(Root, owner.gameObject.scene);
        Scene = Root.GetComponentInChildren<RecipeScene>(true)
            ?? throw new InvalidOperationException("卡牌素材缺少 RecipeScene 引用。");
        // 只保留显示、文字和按钮组件；库存、教程、音乐及场景导航控制器全部禁用。
        foreach (var behaviour in Root.GetComponentsInChildren<Behaviour>(true))
            if (behaviour.TryCast<Constrained2D>() == null && behaviour.TryCast<FastText.FastText>() == null
                && behaviour.TryCast<UIButton>() == null && behaviour.TryCast<TopBar>() == null) behaviour.enabled = false;
        foreach (var collider in Root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var root in owner.gameObject.scene.GetRootGameObjects())
        {
            if (root == Root) continue;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (!renderer.forceRenderingOff) { hidden.Add(renderer); renderer.forceRenderingOff = true; }
        }
        foreach (string name in new[] { "Scene", "TopBar", "CardInventoryLayer", "bg-dark" })
        {
            var child = Root.transform.Find(name)
                ?? throw new InvalidOperationException("卡牌素材缺少显示节点：" + name);
            child.gameObject.SetActive(true);
        }
        Root.SetActive(true);
        return true;
    }

    /// <summary>先确定模板身份再激活；关闭期间也必须完成此步，避免未激活场景堵住 Unity 异步队列。</summary>
    /// <returns>源场景已激活，允许复制或卸载。</returns>
    private bool ActivateSource()
    {
        if (activation == null)
        {
            var source = loading!.Result;
            sourceSceneHandle = source.Scene.handle;
            activation = source.ActivateAsync();
        }
        return activation.isDone;
    }

    /// <summary>加载、关闭及销毁期间都隔离模板控制器；正常卡牌页面不在此范围内。</summary>
    internal static bool IsProtected(Component component)
    {
        if (active == null) return false;
        if (component.gameObject.scene.handle == active.sourceSceneHandle) return true;
        for (var node = component.transform; node != null; node = node.parent)
            if (node.name == RootName) return true;
        return false;
    }

    /// <summary>Awake 阶段隐藏源场景，防止复制之前闪现卡牌界面。</summary>
    internal static void HideSource(RecipeScene scene)
    {
        if (active == null || scene.gameObject.scene.handle != active.sourceSceneHandle) return;
        foreach (var root in scene.gameObject.scene.GetRootGameObjects()) root.SetActive(false);
    }

    /// <summary>控制器每帧在主线程检查取消后的加载及卸载，保留隔离标记直至源场景释放；不注册携带 IL2CPP 非 blittable 句柄的托管回调。</summary>
    internal static void Drain()
    {
        var current = active;
        if (current == null || !current.disposed || current.releaseFailed) return;
        try
        {
            if (current.unloading != null)
            {
                if (!current.unloading.IsDone) return;
                if (current.unloading.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException(current.unloading.OperationException?.Message);
                Addressables.Release(current.unloading);
                active = null;
                return;
            }
            if (current.loading == null || !current.loading.IsDone) return;
            if (current.loading.Status == AsyncOperationStatus.Succeeded)
            {
                if (!current.ActivateSource()) return;
                current.unloading = Addressables.UnloadSceneAsync(current.loading, autoReleaseHandle: false);
                current.loading = null;
            }
            else { Addressables.Release(current.loading); current.loading = null; active = null; }
        }
        catch (Exception ex)
        {
            current.releaseFailed = true;
            Plugin.Logger.LogError($"释放卡牌页面素材失败，请重启游戏后再打开潜力值页面：{ex}");
        }
    }

    /// <summary>先销毁副本，再恢复选歌显示；异步资源由 Drain 完成释放。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (Root != null) { Root.SetActive(false); Object.Destroy(Root); }
        Root = null; Scene = null;
        foreach (var renderer in hidden) if (renderer != null) renderer.forceRenderingOff = false;
        hidden.Clear();
        Drain();
    }
}

/// <summary>模板和副本仅提供序列化显示资源；在 Awake 之前阻断原生音乐、库存和输入初始化。</summary>
[HarmonyPatch]
internal static class PotentialPageCopyLifecyclePatch
{
    /// <summary>仅隔离卡牌模板中的业务控制器；共享原生入口只安装一次补丁，空实现不需要隔离。</summary>
    private static IEnumerable<MethodBase> TargetMethods()
    {
        (Type Type, string[] Methods)[] targets =
        {
            (typeof(RecipeScene), new[] { "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy", "Update" }),
            (typeof(CardInventoryLayer), new[] { "Awake", "OnEnable", "OnDisable", "OnDestroy" }),
            (typeof(CardFilterLayer), new[] { "Awake", "OnEnable", "OnDestroy" }),
            (typeof(CraftingLayer), new[] { "Awake", "Start", "Update", "OnDisable", "OnDestroy" }),
            (typeof(CraftingBonusAreasInformationContainer), new[] { "Start", "OnDestroy" }),
            (typeof(CraftingStrikeInformationContainer), new[] { "Awake", "OnEnable", "Update" }),
            (typeof(GridHoverHexToolTip), new[] { "Awake", "OnDestroy" }),
            (typeof(IotaFilterLayer), new[] { "Awake", "OnEnable", "OnDestroy" }),
            (typeof(IotaInventoryLayer), new[] { "Awake", "OnEnable", "OnDisable", "OnDestroy" }),
            (typeof(IotaScrollAreaContainer), new[] { "Awake", "Update" }),
            (typeof(RecipeFilterLayer), new[] { "Awake", "OnEnable", "OnDestroy" }),
            (typeof(RecipeSelectedCardContainer), new[] { "OnEnable", "OnDestroy" }),
            (typeof(RecipeSelectedLayer), new[] { "Awake", "OnDestroy" }),
            (typeof(RewardBannerV2), new[] { "OnDisable", "OnDestroy" }),
            (typeof(SelectedRecipeLayerScrollContainer), new[] { "Awake", "Update" }),
            (typeof(SmallRecipeButton), new[] { "Awake" })
        };
        var entries = new HashSet<IntPtr>();
        foreach (var (type, methods) in targets)
            foreach (string name in methods)
            {
                var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null) ?? throw new MissingMethodException(type.FullName, name);
                var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
                if (field?.GetValue(null) is not IntPtr info || info == IntPtr.Zero)
                    throw new InvalidOperationException($"无法读取卡牌控制器入口：{type.Name}.{name}。");
                var entry = Marshal.ReadIntPtr(info);
                if (entry == IntPtr.Zero) throw new InvalidOperationException($"卡牌控制器入口为空：{type.Name}.{name}。");
                // Windows x64 的 RET/RET imm16 可能由无关类型共享；拦截它会影响整个进程。
                if (Marshal.ReadByte(entry) is 0xc3 or 0xc2 || !entries.Add(entry)) continue;
                yield return method;
            }
    }

    [HarmonyPrefix]
    private static bool Prefix(Component __instance, MethodBase __originalMethod)
    {
        if (!PotentialPageCopy.IsProtected(__instance)) return true;
        if (__originalMethod.Name == "Awake" && __instance.TryCast<RecipeScene>() is { } scene)
            PotentialPageCopy.HideSource(scene);
        return false;
    }
}
