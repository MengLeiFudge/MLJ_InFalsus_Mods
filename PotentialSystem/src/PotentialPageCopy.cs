using System.Reflection;
using HarmonyLib;
using ifapp.Game.Input;
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
    private readonly Il2CppSystem.Action<AsyncOperationHandle<SceneInstance>> completed;
    private readonly SongSelectScene owner;
    private readonly List<Renderer> hidden = new();
    private bool disposed, releaseFailed;
    internal GameObject? Root { get; private set; }
    internal RecipeScene? Scene { get; private set; }

    /// <summary>独立加载模板，不经过 CoreScene 导航；同一时间只保留一个源场景引用。</summary>
    internal PotentialPageCopy(SongSelectScene owner)
    {
        if (active != null || SceneManager.GetSceneByName("RecipeScene").isLoaded)
            throw new InvalidOperationException("卡牌页面资源正在使用或释放，请稍后再打开。");
        this.owner = owner;
        completed = (Action<AsyncOperationHandle<SceneInstance>>)(_ => Drain());
        active = this;
        try
        {
            loading = Addressables.LoadSceneAsync((Il2CppSystem.String)"RecipeScene", LoadSceneMode.Additive);
            loading.add_Completed(completed);
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

    /// <summary>加载、关闭及销毁期间都隔离模板控制器；正常卡牌页面不在此范围内。</summary>
    internal static bool IsProtected(Component component)
    {
        if (active != null && component.gameObject.scene.name == "RecipeScene") return true;
        for (var node = component.transform; node != null; node = node.parent)
            if (node.name == RootName) return true;
        return false;
    }

    /// <summary>Awake 阶段隐藏源场景，防止复制之前闪现卡牌界面。</summary>
    internal static void HideSource(RecipeScene scene)
    {
        if (active == null || scene.gameObject.scene.name != "RecipeScene") return;
        foreach (var root in scene.gameObject.scene.GetRootGameObjects()) root.SetActive(false);
    }

    /// <summary>取消加载后仍由主线程收尾，保留隔离标记直至源场景彻底卸载。</summary>
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
                current.unloading = Addressables.UnloadSceneAsync(current.loading, autoReleaseHandle: false);
                current.loading = null;
                current.unloading.add_Completed(current.completed);
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
    private static IEnumerable<MethodBase> TargetMethods()
    {
        string[] messages = { "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy", "Update", "LateUpdate" };
        foreach (var type in typeof(RecipeScene).Assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsGenericType || !typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
            if (type.Namespace != typeof(RecipeScene).Namespace && !typeof(InputReceiver).IsAssignableFrom(type)) continue;
            foreach (string name in messages)
            {
                var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (method != null && method.ReturnType == typeof(void)) yield return method;
            }
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
