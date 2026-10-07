using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using ifapp.Game.Common;
using ifapp.Game.Data;
using ifapp.Game.Data.Crafting;
using ifapp.Game.Scenes.Recipe;

namespace InFalsusMod;

/// <summary>在制卡列表和明细的完整刷新期间提供同一筛选视图，返回时恢复真实材料引用。</summary>
internal sealed class SkillFilterNativeHooks : IDisposable
{
    /// <summary>Windows x64 的 _ku 原生 ABI；一次调用同时刷新形状、数量和效能。</summary>
    /// <param name="instance">制卡界面的原生对象。</param>
    /// <param name="method">IL2CPP 隐藏 MethodInfo 参数。</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RefreshCall(IntPtr instance, IntPtr method);

    /// <summary>Windows x64 的 _ju 原生 ABI；details 指向未装箱的 IotaDetails，布尔参数占一字节。</summary>
    /// <param name="instance">制卡界面的原生对象。</param>
    /// <param name="details">IotaDetails 值内存。</param>
    /// <param name="force">原生强制刷新标志。</param>
    /// <param name="method">IL2CPP 隐藏 MethodInfo 参数。</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InstancesCall(IntPtr instance, IntPtr details, byte force, IntPtr method);

    /// <summary>保留原生方法身份，供钩子安装及安全的明细刷新调用共用。</summary>
    private static readonly IntPtr InstancesMethodInfo = MethodInfoPointer(nameof(CraftingLayer._ju));
    /// <summary>持有钩子及回调，防止原生入口仍引用已回收的委托。</summary>
    private INativeDetour? refreshHook, instancesHook;
    /// <summary>未拦截的材料列表完整刷新入口。</summary>
    private readonly RefreshCall originalRefresh;
    /// <summary>未拦截的粒子明细入口。</summary>
    private readonly InstancesCall originalInstances;

    /// <summary>先准备原始委托，再启用钩子；任何初始化失败都撤回已创建的钩子。</summary>
    internal SkillFilterNativeHooks()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("制卡筛选原生入口仅适用于 Windows x64。");
        try
        {
            refreshHook = INativeDetour.Create(Marshal.ReadIntPtr(MethodInfoPointer(nameof(CraftingLayer._ku))),
                (RefreshCall)Refresh);
            originalRefresh = refreshHook.GenerateTrampoline<RefreshCall>();
            instancesHook = INativeDetour.Create(Marshal.ReadIntPtr(InstancesMethodInfo), (InstancesCall)Instances);
            originalInstances = instancesHook.GenerateTrampoline<InstancesCall>();
            refreshHook.Apply();
            instancesHook.Apply();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>从游戏当前生成的互操作方法取得 MethodInfo，不使用固定 RVA 或旧版本地址。</summary>
    /// <param name="name">制卡互操作方法名。</param>
    /// <returns>具有非空原生入口的 MethodInfo 地址。</returns>
    private static IntPtr MethodInfoPointer(string name)
    {
        var method = typeof(CraftingLayer).GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(CraftingLayer).FullName, name);
        var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
        if (field?.GetValue(null) is not IntPtr info || info == IntPtr.Zero)
            throw new InvalidOperationException($"无法读取制卡入口 {name} 的 MethodInfo。");
        // IL2CPP MethodInfo 的首成员为 methodPointer；引用取自运行时生成的互操作程序集。
        if (Marshal.ReadIntPtr(info) == IntPtr.Zero)
            throw new InvalidOperationException($"制卡入口 {name} 为空。");
        return info;
    }

    /// <summary>在一次完整列表刷新内，让形状计算和数量、效能标签读取相同的材料视图。</summary>
    /// <param name="instance">原生制卡界面。</param>
    /// <param name="method">原始 MethodInfo。</param>
    private void Refresh(IntPtr instance, IntPtr method) =>
        WithFilteredInventory(instance, () => originalRefresh(instance, method));

    /// <summary>展开明细及其连带的形状计数使用同一视图，原生详情值地址保持不变。</summary>
    /// <param name="instance">原生制卡界面。</param>
    /// <param name="details">原生粒子定义地址。</param>
    /// <param name="force">原生刷新标志。</param>
    /// <param name="method">原始 MethodInfo。</param>
    private void Instances(IntPtr instance, IntPtr details, byte force, IntPtr method) =>
        WithFilteredInventory(instance, () => originalInstances(instance, details, force, method));

    /// <summary>仅在同步显示调用期间替换可用材料引用；不写入库存或棋盘，异常时亦恢复原引用。</summary>
    /// <param name="instance">读取材料的原生制卡界面。</param>
    /// <param name="refresh">必须在当前调用内完成的原生显示刷新。</param>
    private static void WithFilteredInventory(IntPtr instance, Action refresh)
    {
        CraftingLayer? owner = null;
        UnorderedListVal<IotaInstance>? saved = null, view = null;
        bool replaced = false;
        try
        {
            if (SkillFilter.Active && instance != IntPtr.Zero)
            {
                owner = new CraftingLayer(instance);
                if (SkillFilter.AppliesSelection(owner._gZ.TraitFilter))
                {
                    saved = owner._dZ;
                    view = SkillFilter.View(saved);
                    owner._dZ = view;
                    replaced = true;
                }
            }
        }
        catch (Exception ex)
        {
            SkillFilterPanel.ReportFilterFailure(ex);
        }
        try
        {
            refresh();
        }
        finally
        {
            if (replaced)
                owner!._dZ = saved;
            GC.KeepAlive(view);
            GC.KeepAlive(saved);
        }
    }

    /// <summary>选择变化后刷新已展开的明细，以未装箱的值地址避开当前 by-ref 包装错误。</summary>
    /// <param name="owner">当前制卡界面。</param>
    /// <param name="details">当前展开的粒子定义副本。</param>
    internal static unsafe void RefreshInstances(CraftingLayer owner, IotaDetails details)
    {
        // IotaDetails 的互操作类型是 ValueType 包装；原版 _ju 要求结构体数据而非对象头。
        byte force = 1;
        IntPtr* args = stackalloc IntPtr[2];
        args[0] = IL2CPP.il2cpp_object_unbox(details.Pointer);
        args[1] = (IntPtr)(&force);
        IntPtr error = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(InstancesMethodInfo, owner.Pointer, (void**)args, ref error);
        GC.KeepAlive(details);
        GC.KeepAlive(owner);
        Il2CppException.RaiseExceptionIfNecessary(error);
    }

    /// <summary>撤销钩子并释放原生跳板，亦用于加载失败时回滚。</summary>
    public void Dispose()
    {
        instancesHook?.Dispose();
        instancesHook = null;
        refreshHook?.Dispose();
        refreshHook = null;
    }
}
