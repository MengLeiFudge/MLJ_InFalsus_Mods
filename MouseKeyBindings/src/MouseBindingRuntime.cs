using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using _E;
using ifapp.Game.Input;
using ifapp.Game.Scenes;
using ifapp.Game.UI.Settings;
using GameInputManager = ifapp.Game.Input.InputManager;

namespace InFalsusMod;

/// <summary>在原版输入管理器帧末完成设置捕获、文本显示及游戏轨道转发。</summary>
internal static class MouseBindingRuntime
{
    /// <summary>复用的事件批次，避免没有鼠标按键变化时逐帧分配。</summary>
    private static readonly List<MouseTransition> Transitions = new();

    /// <summary>已向原版输入栈接收者发送按下、仍需保证发送释放的按钮。</summary>
    private static readonly Dictionary<int, ActiveMousePress> ActivePresses = new();

    /// <summary>最近由操作设置页初始化的六轨绑定容器。</summary>
    private static LaneKeyBindSettingsContainer? settingsContainer;

    /// <summary>上一次观察到的等待绑定轨道；负数表示没有槽位在等待。</summary>
    private static int waitingLane = -1;

    /// <summary>开始等待绑定时已入队的最后序号，用于排除选择槽位本身的点击。</summary>
    private static long captureAfterSequence;

    /// <summary>不可恢复的运行异常发生后停止调用游戏输入方法。</summary>
    private static bool disabled;

    /// <summary>本次进程已确认过至少一个鼠标按下进入六轨路径。</summary>
    private static bool gameplayForwardLogged;

    /// <summary>本次进程已确认过至少一个配对释放进入六轨路径。</summary>
    private static bool gameplayReleaseLogged;

    /// <summary>记录设置页当前实例，并立即把保留键值显示为 MouseN。</summary>
    /// <param name="container">刚完成原版绑定刷新的设置容器。</param>
    internal static void RegisterSettings(LaneKeyBindSettingsContainer container)
    {
        if (disabled)
            return;
        settingsContainer = container;
        waitingLane = container._Hh;
        captureAfterSequence = MouseInputEvents.CurrentSequence;
        RefreshLabels(container);
        Plugin.Logger.LogDebug("已接管六轨鼠标按键绑定界面。");
    }

    /// <summary>在原版 InputManager.Update 分发完当前批次后处理鼠标扩展事件。</summary>
    /// <param name="manager">当前游戏输入管理器。</param>
    internal static void Process(GameInputManager manager)
    {
        if (disabled)
            return;

        MouseInputEvents.EnsureDevice(Mouse.current);
        ObserveBindingSlot();
        bool reset = MouseInputEvents.Drain(Transitions);
        if (reset || ActivePresses.Count > 0 && GetPlayableGameScene(manager) == null)
            ReleaseAll(InputState.currentTime);

        foreach (MouseTransition transition in Transitions)
        {
            if (TryCaptureBinding(transition))
                continue;
            ForwardToGame(manager, transition);
        }
        ReleasePhysicallyReleasedButtons(InputState.currentTime);

        if (settingsContainer != null)
            RefreshLabels(settingsContainer);
    }

    /// <summary>记录不可恢复异常并停止后续转发，防止异常跨过游戏的输入回调边界。</summary>
    /// <param name="exception">设置捕获、显示或原版输入转发中的异常。</param>
    internal static void Disable(Exception exception)
    {
        if (disabled)
            return;
        disabled = true;
        ActivePresses.Clear();
        Transitions.Clear();
        settingsContainer = null;
        Plugin.Logger.LogError($"鼠标轨道绑定运行层已停用：{exception}");
    }

    /// <summary>同步玩家当前选择的轨道，并为新的选择隔离首次鼠标点击。</summary>
    private static void ObserveBindingSlot()
    {
        if (settingsContainer == null)
            return;
        if (!settingsContainer.isActiveAndEnabled)
        {
            settingsContainer = null;
            waitingLane = -1;
            captureAfterSequence = MouseInputEvents.CurrentSequence;
            return;
        }

        int currentLane = settingsContainer._Hh;
        if (currentLane == waitingLane)
            return;
        waitingLane = currentLane;
        captureAfterSequence = MouseInputEvents.CurrentSequence;
        if (currentLane >= 0)
            Plugin.Logger.LogDebug($"轨道 {currentLane + 1} 正在等待鼠标按键。");
    }

    /// <summary>在设置页等待键位时，把合格的鼠标按下交给原版弹窗和同键多轨道绑定流程。</summary>
    /// <param name="transition">当前鼠标按钮变化。</param>
    /// <returns>该变化是否已作为设置页绑定被消费。</returns>
    private static bool TryCaptureBinding(MouseTransition transition)
    {
        if (!transition.Pressed || settingsContainer == null || waitingLane < 0
            || transition.Sequence <= captureAfterSequence)
            return false;

        int lane = waitingLane;
        Key key = MouseKeyCodec.Encode(transition.Button);
        var modifiers = default(CurrentKeyboardModifiersState);
        // _iE 先关闭等待键位的 Modal，_HE 的补丁只写入当前轨道，允许多个轨道共用按键。
        settingsContainer._iE(key, transition.Time, ref modifiers);
        var keys = settingsContainer._hh;
        bool stored = keys != null && lane < keys.Length && keys[lane] == key;
        waitingLane = settingsContainer._Hh;
        captureAfterSequence = MouseInputEvents.CurrentSequence;
        RefreshLabels(settingsContainer);
        if (stored)
            Plugin.Logger.LogDebug($"轨道 {lane + 1} 已绑定 Mouse{transition.Button}。");
        else
            Plugin.Logger.LogDebug($"Mouse{transition.Button} 未写入轨道 {lane + 1}。");
        return true;
    }

    /// <summary>仅把当前六轨绑定的 MouseN 转发给原版始终接收输入的非暂停 GameScene。</summary>
    /// <param name="manager">提供始终接收栈和全局输入状态的管理器。</param>
    /// <param name="transition">带原始事件时间的鼠标按钮变化。</param>
    private static void ForwardToGame(GameInputManager manager, MouseTransition transition)
    {
        if (!transition.Pressed)
        {
            Release(transition.Button, transition.Time);
            return;
        }
        if (ActivePresses.ContainsKey(transition.Button))
            return;

        Key key = MouseKeyCodec.Encode(transition.Button);
        GameScene? scene = GetPlayableGameScene(manager);
        if (scene == null || !IsLaneBinding(key))
            return;

        CurrentKeyboardModifiersState modifiers = transition.Modifiers;
        // 六轨命中会先点亮 Track；返回值来自随后执行的通用输入处理，不能代表轨道是否接收。
        scene._OvA(key, transition.Time, ref modifiers);
        ActivePresses.Add(transition.Button,
            new ActiveMousePress(transition.Device, scene, key, modifiers));
        if (!gameplayForwardLogged)
        {
            gameplayForwardLogged = true;
            Plugin.Logger.LogDebug($"Mouse{transition.Button} 已进入原版六轨按下路径。");
        }
    }

    /// <summary>取得已登记为始终接收者且暂停层未显示的当前游玩场景。</summary>
    /// <param name="manager">拥有原版输入所有权数组的管理器。</param>
    /// <returns>可接收鼠标轨道输入的 GameScene；条件不满足时返回 null。</returns>
    private static GameScene? GetPlayableGameScene(GameInputManager manager)
    {
        if (!manager.IsAllInputEnabled())
            return null;

        GameScene? scene = GameScene._xn;
        if (scene == null || !scene.isActiveAndEnabled)
            return null;
        var pauseLayer = scene.pauseLayer;
        if (pauseLayer != null && pauseLayer._a)
            return null;

        var receivers = manager.inputAlwaysReceivedStack;
        if (receivers == null)
            return null;
        int count = Math.Min(manager.inputAlwaysReceivedStackCount, receivers.Length);
        for (int i = 0; i < count; i++)
        {
            InputReceiver? receiver = receivers[i]?.Owner;
            if (receiver != null && receiver.Pointer == scene.Pointer)
                return scene;
        }
        return null;
    }

    /// <summary>确认保留键值当前确实位于原版六轨键位数组中。</summary>
    /// <param name="key">MouseN 对应的保留 Key 值。</param>
    /// <returns>至少一条轨道当前是否绑定该值。</returns>
    private static bool IsLaneBinding(Key key)
    {
        var laneKeys = _YD._JfA;
        if (laneKeys == null)
            return false;
        int count = Math.Min(6, laneKeys.Length);
        for (int i = 0; i < count; i++)
            if (laneKeys[i] == key)
                return true;
        return false;
    }

    /// <summary>在原始释放事件缺失时，以 Input System 当前物理状态补齐释放。</summary>
    /// <param name="time">补偿释放使用的当前 Input System 时间，单位秒。</param>
    private static void ReleasePhysicallyReleasedButtons(double time)
    {
        int[] buttons = ActivePresses
            .Where(pair => MouseInputEvents.TryGetCurrentButtonState(
                pair.Value.Device, pair.Key, out bool pressed) && !pressed)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (int button in buttons)
            Release(button, time);
    }

    /// <summary>向接收过按下的同一 GameScene 发送释放，即使期间暂停层已经出现。</summary>
    /// <param name="button">从零开始的鼠标按钮号。</param>
    /// <param name="time">释放事件的 Input System 时间，单位秒。</param>
    private static void Release(int button, double time)
    {
        if (!ActivePresses.Remove(button, out ActiveMousePress press) || press.Receiver == null)
            return;

        CurrentKeyboardModifiersState modifiers = press.Modifiers;
        press.Receiver._fwA(press.Key, time, ref modifiers);
        if (!gameplayReleaseLogged)
        {
            gameplayReleaseLogged = true;
            Plugin.Logger.LogDebug($"Mouse{button} 已进入原版六轨释放路径。");
        }
    }

    /// <summary>输入队列失去连续性时释放全部已转发按键，避免轨道保持按下。</summary>
    /// <param name="time">恢复释放所使用的当前 Input System 时间，单位秒。</param>
    private static void ReleaseAll(double time)
    {
        int[] buttons = ActivePresses.Keys.ToArray();
        foreach (int button in buttons)
            Release(button, time);
    }

    /// <summary>
    /// 复刻 1.0.6 原版 _GE：逐轨写入键名，按与已保存绑定（_YD._JfA）是否不同设置轨道按钮高亮，
    /// 任一轨道改动时启用应用按钮。键名对鼠标保留值改用 MouseN，避免 Keyboard 索引越界。
    /// </summary>
    /// <param name="container">六轨绑定设置容器。</param>
    /// <returns>已接管刷新时为真；没有鼠标绑定或数据未就绪时为假，交回原版处理。</returns>
    internal static bool RefreshBindingView(LaneKeyBindSettingsContainer container)
    {
        var keys = container._hh;
        var buttons = container.laneButtons;
        var labels = container.laneButtonTexts;
        if (keys == null || buttons == null || labels == null || buttons.Length == 0)
            return false;
        bool hasMouse = false;
        for (int lane = 0; lane < keys.Length; lane++)
            hasMouse |= MouseKeyCodec.TryDecode(keys[lane], out _);
        if (!hasMouse)
            return false;

        var keyboard = Keyboard.current;
        int labelsPerLane = labels.Length / buttons.Length;
        for (int lane = 0; lane < labels.Length / Math.Max(1, labelsPerLane) && lane < keys.Length; lane++)
        {
            Key key = keys[lane];
            string text = MouseKeyCodec.TryDecode(key, out int button) ? $"Mouse{button}"
                : keyboard?[key]?.displayName ?? key.ToString();
            for (int copy = 0; copy < labelsPerLane; copy++)
                labels[lane * labelsPerLane + copy]?.SetTextNonLocalized(text, FastText.FastText.UpdateType.Default);
        }

        var saved = _YD._JfA;
        bool changed = false;
        for (int lane = 0; lane < buttons.Length && lane < keys.Length; lane++)
        {
            bool differs = saved == null || lane >= saved.Length || saved[lane] != keys[lane];
            changed |= differs;
            buttons[lane]?._dSA(differs ? _m._dI._zIb : _m._dI._YIb);
        }
        container.applyButton?._dSA(changed ? _m._dI._YIb : _m._dI._yIb);
        return true;
    }

    /// <summary>把原版为保留枚举值生成的数字文本替换为稳定的 MouseN 标签。</summary>
    /// <param name="container">拥有六轨临时绑定和全部语言文本实例的设置容器。</param>
    private static void RefreshLabels(LaneKeyBindSettingsContainer container)
    {
        var keys = container._hh;
        var labels = container.laneButtonTexts;
        if (keys == null || labels == null || keys.Length == 0 || labels.Length % keys.Length != 0)
            return;

        int labelsPerLane = labels.Length / keys.Length;
        for (int lane = 0; lane < keys.Length; lane++)
        {
            if (!MouseKeyCodec.TryDecode(keys[lane], out int button))
                continue;
            string text = $"Mouse{button}";
            int firstLabel = lane * labelsPerLane;
            for (int copy = 0; copy < labelsPerLane; copy++)
            {
                FastText.FastText? label = labels[firstLabel + copy];
                if (label != null && !string.Equals(label.GetText(), text, StringComparison.Ordinal))
                    label.SetTextNonLocalized(text, FastText.FastText.UpdateType.Default);
            }
        }
    }

    /// <summary>一次已进入原版 GameScene 六轨路径、等待配对释放的鼠标按下。</summary>
    /// <param name="Device">产生按下的 Input System 鼠标设备指针。</param>
    /// <param name="Receiver">接收按下的原版 GameScene。</param>
    /// <param name="Key">该按钮对应的保留 Key 值。</param>
    /// <param name="Modifiers">按下时用于匹配原版绑定的修饰键状态。</param>
    private readonly record struct ActiveMousePress(
        IntPtr Device,
        GameScene Receiver,
        Key Key,
        CurrentKeyboardModifiersState Modifiers);
}
