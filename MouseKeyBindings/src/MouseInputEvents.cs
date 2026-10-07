using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using ifapp.Game.Input;
using GameInputManager = ifapp.Game.Input.InputManager;

namespace InFalsusMod;

/// <summary>一项按原始 Input System 时间记录的鼠标按钮状态变化。</summary>
/// <param name="Device">产生该变化的 Input System 鼠标设备指针。</param>
/// <param name="Button">从零开始的鼠标按钮号。</param>
/// <param name="Pressed">按下为真，释放为假。</param>
/// <param name="Time">Input System 事件时间，单位秒。</param>
/// <param name="Sequence">该按钮变化在本模组内的单调序号。</param>
/// <param name="Modifiers">事件发生时游戏已记录的键盘修饰键状态。</param>
internal readonly record struct MouseTransition(
    IntPtr Device,
    int Button,
    bool Pressed,
    double Time,
    long Sequence,
    CurrentKeyboardModifiersState Modifiers);

/// <summary>从游戏已经订阅的 Input System 事件中提取原生按钮位和额外 ButtonControl。</summary>
internal static class MouseInputEvents
{
    /// <summary>Input System 的 <c>StateEvent</c> FourCC：STAT。</summary>
    private const uint StateEventCode = 0x54415453;

    /// <summary>Input System 的 <c>DeltaStateEvent</c> FourCC：DLTA。</summary>
    private const uint DeltaStateEventCode = 0x41544c44;

    /// <summary><c>MouseState.buttons</c> 原生字段中可直接寻址的按钮位数。</summary>
    private const int RawMouseButtonCount = 16;

    /// <summary>StateEvent 固定头部长度，后续字节从设备状态偏移零开始。</summary>
    private const int StateEventHeaderSize = 24;

    /// <summary>DeltaStateEvent 固定头部长度，后续字节从事件指定的状态偏移开始。</summary>
    private const int DeltaStateEventHeaderSize = 28;

    /// <summary>防止消费补丁失效时按钮变化无限积压。</summary>
    private const int MaximumPendingTransitions = 256;

    /// <summary>保护设备快照和待处理队列；Input System 回调不依赖一定运行于主线程。</summary>
    private static readonly object Sync = new();

    /// <summary>每个原生鼠标设备最近一次观察到的按钮状态及扩展控件。</summary>
    private static readonly Dictionary<IntPtr, MouseDeviceState> Devices = new();

    /// <summary>等待 InputManager.Update 原版输入分发结束后处理的按钮变化。</summary>
    private static readonly Queue<MouseTransition> Pending = new();

    /// <summary>已加入队列的最后一项按钮变化序号。</summary>
    private static long lastSequence;

    /// <summary>队列溢出后要求运行层释放全部已转发按键，避免轨道停留在按下状态。</summary>
    private static bool resetRequested;

    /// <summary>发生不兼容异常后停用事件提取，避免在高频鼠标移动中重复抛错。</summary>
    private static bool disabled;

    /// <summary>在尚无鼠标事件时建立当前设备快照，避免首个事件被当成未知初始状态。</summary>
    /// <param name="mouse">Input System 当前鼠标；没有鼠标时可为 <see langword="null"/>。</param>
    internal static void EnsureDevice(Mouse? mouse)
    {
        if (disabled || mouse == null)
            return;
        try
        {
            lock (Sync)
                GetOrCreateDevice(mouse);
        }
        catch (Exception ex)
        {
            Disable(ex);
        }
    }

    /// <summary>读取一项原始设备事件；非鼠标事件和不含按钮字节的事件直接忽略。</summary>
    /// <param name="manager">维护事件顺序和当前键盘状态的游戏输入管理器。</param>
    /// <param name="eventPtr">游戏收到的原始 Input System 事件。</param>
    /// <param name="device">该事件所属的 Input System 设备。</param>
    internal static unsafe void Observe(GameInputManager manager, InputEventPtr eventPtr, InputDevice? device)
    {
        if (disabled || device == null || eventPtr.m_EventPtr == IntPtr.Zero)
            return;
        try
        {
            var mouse = device.TryCast<Mouse>();
            if (mouse == null)
                return;

            lock (Sync)
            {
                var state = GetOrCreateDevice(mouse);
                byte* eventData = (byte*)eventPtr.m_EventPtr;
                bool contextRead = false;
                double time = 0;
                var modifiers = default(CurrentKeyboardModifiersState);
                ushort covered = 0;

                foreach (StandardButtonState standard in state.StandardButtons)
                {
                    if (!InputControlExtensions.ReadValueFromEvent(standard.Control, eventPtr, out float value))
                        continue;
                    ushort mask = (ushort)(1 << standard.Number);
                    covered |= mask;
                    bool wasPressed = (state.Buttons & mask) != 0;
                    float threshold = wasPressed
                        ? standard.Control.pressPointOrDefault * ButtonControl.s_GlobalDefaultButtonReleaseThreshold
                        : standard.Control.pressPointOrDefault;
                    bool pressed = value >= threshold;
                    if (pressed == wasPressed)
                        continue;
                    state.Buttons = pressed
                        ? (ushort)(state.Buttons | mask)
                        : (ushort)(state.Buttons & ~mask);
                    QueueTransition(manager, eventPtr, mouse.Pointer, standard.Number, pressed,
                        ref contextRead, ref time, ref modifiers);
                }

                if (TryReadButtons(eventData, state, out ushort buttons))
                {
                    ushort uncovered = (ushort)~covered;
                    ushort changed = (ushort)((buttons ^ state.Buttons) & uncovered);
                    state.Buttons = (ushort)((state.Buttons & covered) | (buttons & uncovered));
                    for (int button = 0; button < RawMouseButtonCount; button++)
                    {
                        ushort mask = (ushort)(1 << button);
                        if ((changed & mask) != 0)
                            QueueTransition(manager, eventPtr, mouse.Pointer, button, (buttons & mask) != 0,
                                ref contextRead, ref time, ref modifiers);
                    }
                }

                foreach (ExtraButtonState extra in state.ExtraButtons)
                {
                    if (!InputControlExtensions.ReadValueFromEvent(extra.Control, eventPtr, out float value))
                        continue;
                    float threshold = extra.Pressed
                        ? extra.Control.pressPointOrDefault * ButtonControl.s_GlobalDefaultButtonReleaseThreshold
                        : extra.Control.pressPointOrDefault;
                    bool pressed = value >= threshold;
                    if (pressed == extra.Pressed)
                        continue;
                    extra.Pressed = pressed;
                    QueueTransition(manager, eventPtr, mouse.Pointer, extra.Number, pressed,
                        ref contextRead, ref time, ref modifiers);
                }
            }
        }
        catch (Exception ex)
        {
            Disable(ex);
        }
    }

    /// <summary>读取最后一项已入队变化的序号，用于隔离选择绑定槽的既有鼠标事件。</summary>
    internal static long CurrentSequence
    {
        get
        {
            lock (Sync)
                return lastSequence;
        }
    }

    /// <summary>把当前帧积累的按钮变化移交给运行层。</summary>
    /// <param name="destination">由调用者复用的目标列表。</param>
    /// <returns>是否因队列溢出而需要先释放全部活动按键。</returns>
    internal static bool Drain(List<MouseTransition> destination)
    {
        lock (Sync)
        {
            destination.Clear();
            while (Pending.Count > 0)
                destination.Add(Pending.Dequeue());
            bool reset = resetRequested;
            resetRequested = false;
            return reset;
        }
    }

    /// <summary>为一个新鼠标设备确定按钮字节在设备状态中的相对偏移。</summary>
    /// <param name="mouse">要建立快照的鼠标设备。</param>
    /// <returns>可持续更新的设备按钮快照。</returns>
    private static MouseDeviceState GetOrCreateDevice(Mouse mouse)
    {
        if (Devices.TryGetValue(mouse.Pointer, out var existing))
            return existing;

        var standardButtons = new[]
        {
            new StandardButtonState(mouse.leftButton, 0),
            new StandardButtonState(mouse.rightButton, 1),
            new StandardButtonState(mouse.middleButton, 2),
            new StandardButtonState(mouse.forwardButton, 3),
            new StandardButtonState(mouse.backButton, 4)
        };
        ushort buttons = 0;
        foreach (StandardButtonState standard in standardButtons)
            if (standard.Control.isPressed)
                buttons |= (ushort)(1 << standard.Number);

        var deviceBlock = mouse.stateBlock;
        var leftButtonBlock = mouse.leftButton.stateBlock;
        int buttonOffset = -1;
        ulong? rawButtonBaseBit = null;
        if (leftButtonBlock.bitOffset == 0 && leftButtonBlock.sizeInBits == 1
            && leftButtonBlock.byteOffset >= deviceBlock.byteOffset)
        {
            uint offset = leftButtonBlock.byteOffset - deviceBlock.byteOffset;
            if (offset <= int.MaxValue)
            {
                buttonOffset = (int)offset;
                rawButtonBaseBit = (ulong)offset * 8;
            }
        }
        uint stateFormat = unchecked((uint)deviceBlock.format.m_Code);
        var extraButtons = DiscoverExtraButtons(
            mouse, deviceBlock.byteOffset, rawButtonBaseBit, standardButtons);
        var created = new MouseDeviceState(buttonOffset, stateFormat, buttons, standardButtons, extraButtons);
        Devices.Add(mouse.Pointer, created);
        return created;
    }

    /// <summary>发现原生 16 位按钮字段之外由鼠标布局额外公开的 ButtonControl。</summary>
    /// <param name="mouse">要检查的鼠标设备。</param>
    /// <param name="deviceByteOffset">设备状态在全局缓冲区中的起始字节。</param>
    /// <param name="rawButtonBaseBit">布局兼容时 Mouse0 在设备局部状态中的位偏移。</param>
    /// <param name="standardButtons">已经映射为 Mouse0 至 Mouse4 的标准控件。</param>
    /// <returns>按稳定 MouseN 排序并去除状态别名后的额外按钮。</returns>
    private static List<ExtraButtonState> DiscoverExtraButtons(
        Mouse mouse,
        uint deviceByteOffset,
        ulong? rawButtonBaseBit,
        IReadOnlyList<StandardButtonState> standardButtons)
    {
        var standardPointers = standardButtons.Select(button => button.Control.Pointer).ToHashSet();
        var candidates = new List<ButtonCandidate>();
        var controls = mouse.allControls;
        for (int index = 0; index < controls.Count; index++)
        {
            InputControl? control = controls[index];
            ButtonControl? button = control?.TryCast<ButtonControl>();
            if (button == null || standardPointers.Contains(button.Pointer))
                continue;

            var block = button.stateBlock;
            if (block.byteOffset == uint.MaxValue || block.sizeInBits == 0
                || block.byteOffset < deviceByteOffset)
                continue;
            ulong startBit = ((ulong)block.byteOffset - deviceByteOffset) * 8 + block.bitOffset;
            if (rawButtonBaseBit.HasValue
                && startBit >= rawButtonBaseBit.Value
                && startBit < rawButtonBaseBit.Value + RawMouseButtonCount)
                continue;
            candidates.Add(new ButtonCandidate(button, startBit, block.sizeInBits, button.path ?? string.Empty));
        }

        candidates.Sort(static (left, right) =>
        {
            int offsetOrder = left.StartBit.CompareTo(right.StartBit);
            if (offsetOrder != 0)
                return offsetOrder;
            int sizeOrder = left.SizeInBits.CompareTo(right.SizeInBits);
            return sizeOrder != 0 ? sizeOrder : string.CompareOrdinal(left.Path, right.Path);
        });

        var unique = new List<ButtonCandidate>();
        var occupiedRanges = new HashSet<(ulong StartBit, uint SizeInBits)>();
        foreach (ButtonCandidate candidate in candidates)
            if (occupiedRanges.Add((candidate.StartBit, candidate.SizeInBits)))
                unique.Add(candidate);

        var result = new List<ExtraButtonState>(unique.Count);
        var usedNumbers = new HashSet<int>(Enumerable.Range(0, RawMouseButtonCount));
        var assignedControls = new HashSet<IntPtr>();
        foreach (ButtonCandidate candidate in unique)
        {
            if (!rawButtonBaseBit.HasValue
                || !TryGetNaturalNumber(candidate, rawButtonBaseBit.Value, out int number)
                || !usedNumbers.Add(number))
                continue;
            result.Add(new ExtraButtonState(candidate.Control, number, candidate.Control.isPressed));
            assignedControls.Add(candidate.Control.Pointer);
        }

        int nextNumber = RawMouseButtonCount;
        foreach (ButtonCandidate candidate in unique)
        {
            if (assignedControls.Contains(candidate.Control.Pointer))
                continue;
            while (usedNumbers.Contains(nextNumber))
                nextNumber++;
            if (nextNumber > MouseKeyCodec.MaximumMouseButton)
                throw new NotSupportedException("当前 Mouse 布局公开的按钮数量超出可持久化范围。");
            usedNumbers.Add(nextNumber);
            result.Add(new ExtraButtonState(candidate.Control, nextNumber, candidate.Control.isPressed));
            nextNumber++;
        }

        result.Sort(static (left, right) => left.Number.CompareTo(right.Number));
        return result;
    }

    /// <summary>让一位宽的扩展按钮沿用其相对 Mouse0 的状态位编号。</summary>
    /// <param name="candidate">待编号的按钮控件。</param>
    /// <param name="rawButtonBaseBit">Mouse0 在设备局部状态中的位偏移。</param>
    /// <param name="number">成功时为稳定的 MouseN 编号。</param>
    /// <returns>该控件能否直接使用状态位编号。</returns>
    private static bool TryGetNaturalNumber(ButtonCandidate candidate, ulong rawButtonBaseBit, out int number)
    {
        number = -1;
        if (candidate.SizeInBits != 1 || candidate.StartBit < rawButtonBaseBit)
            return false;
        ulong relativeBit = candidate.StartBit - rawButtonBaseBit;
        if (relativeBit > MouseKeyCodec.MaximumMouseButton)
            return false;
        number = (int)relativeBit;
        return true;
    }

    /// <summary>从完整或增量状态事件更新鼠标的两个原生按钮字节。</summary>
    /// <param name="eventData">InputEvent 原生头部首地址。</param>
    /// <param name="state">对应鼠标设备的既有快照及按钮偏移。</param>
    /// <param name="buttons">事件应用后的 16 位按钮状态。</param>
    /// <returns>事件是否覆盖了任一按钮状态字节。</returns>
    private static unsafe bool TryReadButtons(byte* eventData, MouseDeviceState state, out ushort buttons)
    {
        buttons = state.Buttons;
        if (state.ButtonOffset < 0)
            return false;
        uint type = *(uint*)eventData;
        int eventSize = *(ushort*)(eventData + 4);
        if (eventSize < StateEventHeaderSize
            || (type != StateEventCode && type != DeltaStateEventCode)
            || *(uint*)(eventData + 20) != state.StateFormat)
            return false;
        if (type == StateEventCode)
        {
            int stateSize = eventSize - StateEventHeaderSize;
            if (state.ButtonOffset < 0 || state.ButtonOffset + sizeof(ushort) > stateSize)
                return false;
            byte* source = eventData + StateEventHeaderSize + state.ButtonOffset;
            buttons = (ushort)(source[0] | source[1] << 8);
            return true;
        }

        if (eventSize < DeltaStateEventHeaderSize)
            return false;

        uint deltaOffset = *(uint*)(eventData + 24);
        int deltaSize = eventSize - DeltaStateEventHeaderSize;
        bool covered = false;
        for (int byteIndex = 0; byteIndex < sizeof(ushort); byteIndex++)
        {
            uint stateOffset = (uint)(state.ButtonOffset + byteIndex);
            if (stateOffset < deltaOffset || stateOffset - deltaOffset >= deltaSize)
                continue;
            byte value = eventData[DeltaStateEventHeaderSize + stateOffset - deltaOffset];
            int shift = byteIndex * 8;
            buttons = (ushort)((buttons & ~(0xff << shift)) | value << shift);
            covered = true;
        }
        return covered;
    }

    /// <summary>读取按钮当前物理状态，并用它校正事件快照。</summary>
    /// <param name="device">产生按下事件的鼠标设备指针。</param>
    /// <param name="button">从零开始的 MouseN 编号。</param>
    /// <param name="pressed">成功时为 Input System 当前按钮状态。</param>
    /// <returns>能否在该设备中定位按钮。</returns>
    internal static bool TryGetCurrentButtonState(IntPtr device, int button, out bool pressed)
    {
        lock (Sync)
        {
            pressed = false;
            if (!Devices.TryGetValue(device, out MouseDeviceState? state))
                return false;

            foreach (StandardButtonState standard in state.StandardButtons)
            {
                if (standard.Number != button)
                    continue;
                pressed = standard.Control.isPressed;
                ushort mask = (ushort)(1 << button);
                state.Buttons = pressed
                    ? (ushort)(state.Buttons | mask)
                    : (ushort)(state.Buttons & ~mask);
                return true;
            }

            foreach (ExtraButtonState extra in state.ExtraButtons)
            {
                if (extra.Number != button)
                    continue;
                pressed = extra.Control.isPressed;
                extra.Pressed = pressed;
                return true;
            }

            if ((uint)button >= RawMouseButtonCount)
                return false;
            pressed = (state.Buttons & 1 << button) != 0;
            return true;
        }
    }

    /// <summary>为当前事件首次读取时间和修饰键，再加入一项按钮变化。</summary>
    /// <param name="manager">按事件顺序维护键盘状态的游戏输入管理器。</param>
    /// <param name="eventPtr">当前 Input System 事件。</param>
    /// <param name="device">产生事件的鼠标设备指针。</param>
    /// <param name="button">稳定的 MouseN 编号。</param>
    /// <param name="pressed">按下为真，释放为假。</param>
    /// <param name="contextRead">当前事件的公共上下文是否已经读取。</param>
    /// <param name="time">当前事件时间，单位秒。</param>
    /// <param name="modifiers">当前事件的键盘修饰键状态。</param>
    private static void QueueTransition(
        GameInputManager manager,
        InputEventPtr eventPtr,
        IntPtr device,
        int button,
        bool pressed,
        ref bool contextRead,
        ref double time,
        ref CurrentKeyboardModifiersState modifiers)
    {
        if (!contextRead)
        {
            time = eventPtr.time;
            modifiers = ReadModifiers(manager);
            contextRead = true;
        }
        Enqueue(new MouseTransition(device, button, pressed, time, ++lastSequence, modifiers));
    }

    /// <summary>按游戏自己的键盘快照生成与原版事件相同的修饰键位标记。</summary>
    /// <param name="manager">已经按事件顺序更新键盘状态的输入管理器。</param>
    /// <returns>Ctrl、Alt 和 Shift 的组合状态。</returns>
    private static CurrentKeyboardModifiersState ReadModifiers(GameInputManager manager)
    {
        UnityKeyboardState keyboard = manager.currentKeyboardState;
        var modifiers = default(CurrentKeyboardModifiersState);
        modifiers.IsCtrlPressed = keyboard.IsPressed(Key.LeftCtrl) || keyboard.IsPressed(Key.RightCtrl);
        modifiers.IsAltPressed = keyboard.IsPressed(Key.LeftAlt) || keyboard.IsPressed(Key.RightAlt);
        modifiers.IsShiftPressed = keyboard.IsPressed(Key.LeftShift) || keyboard.IsPressed(Key.RightShift);
        return modifiers;
    }

    /// <summary>加入一项变化；积压异常时丢弃整批并通知运行层恢复释放状态。</summary>
    /// <param name="transition">待处理的鼠标按钮变化。</param>
    private static void Enqueue(MouseTransition transition)
    {
        if (Pending.Count >= MaximumPendingTransitions)
        {
            Pending.Clear();
            resetRequested = true;
        }
        Pending.Enqueue(transition);
    }

    /// <summary>记录一次事件布局不兼容并停止高频事件解析。</summary>
    /// <param name="exception">导致鼠标事件提取无法继续的异常。</param>
    private static void Disable(Exception exception)
    {
        lock (Sync)
            disabled = true;
        Plugin.Logger.LogError($"鼠标按钮事件提取已停用：{exception}");
    }

    /// <summary>一个鼠标设备的按钮字节偏移和最近状态。</summary>
    private sealed class MouseDeviceState
    {
        /// <summary>按钮低字节相对设备状态起点的偏移；负数表示布局不支持原始16位读取。</summary>
        internal int ButtonOffset { get; }

        /// <summary>设备根状态和事件必须一致的 FourCC。</summary>
        internal uint StateFormat { get; }

        /// <summary>Mouse0 至 Mouse4 的标准 Input System 按钮控件。</summary>
        internal IReadOnlyList<StandardButtonState> StandardButtons { get; }

        /// <summary>当前设备在原生 16 位字段之外公开的按钮控件。</summary>
        internal IReadOnlyList<ExtraButtonState> ExtraButtons { get; }

        /// <summary>最近一次完整应用后的 Mouse0 至 Mouse15 位状态。</summary>
        internal ushort Buttons { get; set; }

        /// <summary>创建设备按钮快照。</summary>
        /// <param name="buttonOffset">按钮低字节相对设备状态起点的偏移；负数时只读取公开控件。</param>
        /// <param name="stateFormat">设备根状态所声明的 FourCC。</param>
        /// <param name="buttons">创建时可从公开控件读取的原生按钮状态。</param>
        /// <param name="standardButtons">Mouse0 至 Mouse4 的标准按钮控件。</param>
        /// <param name="extraButtons">按稳定 MouseN 编号排序的额外按钮控件。</param>
        internal MouseDeviceState(
            int buttonOffset,
            uint stateFormat,
            ushort buttons,
            IReadOnlyList<StandardButtonState> standardButtons,
            IReadOnlyList<ExtraButtonState> extraButtons)
        {
            ButtonOffset = buttonOffset;
            StateFormat = stateFormat;
            Buttons = buttons;
            StandardButtons = standardButtons;
            ExtraButtons = extraButtons;
        }
    }

    /// <summary>通过 Input System 公共控件读取的标准鼠标按钮。</summary>
    private sealed class StandardButtonState
    {
        /// <summary>用于读取当前事件值和按压阈值的按钮控件。</summary>
        internal ButtonControl Control { get; }

        /// <summary>Mouse0 至 Mouse4 的稳定编号。</summary>
        internal int Number { get; }

        /// <summary>保存标准鼠标按钮与 MouseN 编号的固定对应关系。</summary>
        /// <param name="control">Input System 标准按钮控件。</param>
        /// <param name="number">从零开始的 MouseN 编号。</param>
        internal StandardButtonState(ButtonControl control, int number)
        {
            Control = control;
            Number = number;
        }
    }

    /// <summary>用于稳定排序和去除状态别名的额外按钮候选。</summary>
    /// <param name="Control">Unity 鼠标布局公开的按钮控件。</param>
    /// <param name="StartBit">控件相对设备状态起点的位偏移。</param>
    /// <param name="SizeInBits">控件在设备状态中占用的位数。</param>
    /// <param name="Path">状态位置相同时用于稳定排序的控件路径。</param>
    private readonly record struct ButtonCandidate(
        ButtonControl Control,
        ulong StartBit,
        uint SizeInBits,
        string Path);

    /// <summary>一个原生 16 位字段之外、可单独绑定的鼠标按钮控件。</summary>
    private sealed class ExtraButtonState
    {
        /// <summary>用于从当前事件读取已处理浮点值的 Input System 控件。</summary>
        internal ButtonControl Control { get; }

        /// <summary>持久化和显示所使用的 MouseN 编号。</summary>
        internal int Number { get; }

        /// <summary>最近一次按控件阈值判断出的按下状态。</summary>
        internal bool Pressed { get; set; }

        /// <summary>创建额外按钮状态。</summary>
        /// <param name="control">Input System 按钮控件。</param>
        /// <param name="number">稳定的 MouseN 编号。</param>
        /// <param name="pressed">创建时的按下状态。</param>
        internal ExtraButtonState(ButtonControl control, int number, bool pressed)
        {
            Control = control;
            Number = number;
            Pressed = pressed;
        }
    }
}
