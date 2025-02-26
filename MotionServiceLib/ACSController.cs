using ACS.SPiiPlusNET;
using MotionServiceLib;
using Serilog;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;



public class AcsLib
{
    public Api Ch;
    public int AxisNum;
    public ACS.SPiiPlusNET.Axis Axis;
    public bool bConnected;
    private Thread MotorStateThr;
    private ManualResetEvent DoMonitorEvent;
    private ManualResetEvent MotorStateThreadIdleEvent;

    private double previousPosition;
    private bool previousIsEnabled;
    private bool previousIsMoving;

    public event Action<string> ErrorOccurred;
    public event Action<int, double, bool, bool> MotorStateChanged;
    public event Action<bool> ConnectionStatusChanged;


    public event Action MotorEnabled;
    public event Action MotorDisabled;
    public event Action MotorStopped; // New event for motor stopped

    private AutoResetEvent allAxesIdleEvent;

    public event Action AllAxesIdle;

    public string name;
    private readonly ILogger _logger;


    private const int IDLE_CHECK_INTERVAL_MS = 100;
    private const int IDLE_TIMEOUT_MS = 10000; // 30 seconds timeout
    public AcsLib(string name)
    {
        _logger = Log.ForContext<AcsLib>();
        _logger.Information("Initializing ACSController with name: {Name}", name);

        Ch = new Api();
        AxisNum = 0;
        Axis = (ACS.SPiiPlusNET.Axis)AxisNum;
        DoMonitorEvent = new ManualResetEvent(false);
        MotorStateThreadIdleEvent = new ManualResetEvent(true);
        MotorStateThr = new Thread(MotorStateThreadProc);
        MotorStateThr.IsBackground = true;
        MotorStateThr.Start();
        bConnected = false;

        previousPosition = double.NaN;
        previousIsEnabled = false;
        previousIsMoving = false;

        allAxesIdleEvent = new AutoResetEvent(false);
        AllAxesIdle += OnAllAxesIdle;
        this.name = name;

        _logger.Information("ACSController initialization completed");
    }

    private void OnAllAxesIdle()
    {
        _logger.Information("All axes are now idle");
        allAxesIdleEvent.Set();
    }

    public void WaitForAllAxesIdle()
    {
        _logger.Information("Waiting for all axes to become idle");
        allAxesIdleEvent.WaitOne();
        _logger.Information("All axes are now idle");
    }
    public async Task WaitForAllAxesIdleAsync()
    {
        _logger.Information("Waiting for all axes to become idle");
        var startTime = DateTime.Now;

        while (true)
        {
            bool allAxesIdle = true;

            for (int axis = 0; axis < 3; axis++) // Assuming 3 axes
            {
                var (_, _, isMoving) = GetAxisStatus(axis);
                if (isMoving)
                {
                    allAxesIdle = false;
                    break;
                }
            }

            if (allAxesIdle)
            {
                _logger.Information("All axes are now idle");
                return;
            }

            if ((DateTime.Now - startTime).TotalMilliseconds > IDLE_TIMEOUT_MS)
            {
                _logger.Warning("Timeout waiting for axes to become idle");
                return;
            }

            await Task.Delay(IDLE_CHECK_INTERVAL_MS);
        }
    }
    public void Connect(string commType, string address, int baudRate = -1, int slotNumber = -1)
    {
        _logger.Information("Attempting to connect using {CommType} to {Address}", commType, address);
        try
        {
            switch (commType)
            {
                case "Serial":
                    _logger.Debug("Opening serial communication with baud rate: {BaudRate}", baudRate);
                    Ch.OpenCommSerial(1, baudRate);
                    break;
                case "Ethernet":
                    _logger.Debug("Opening Ethernet communication");
                    int protocol = (int)EthernetCommOption.ACSC_SOCKET_DGRAM_PORT;
                    Ch.OpenCommEthernet(address, protocol);
                    break;
                case "PCI":
                    _logger.Debug("Opening PCI communication with slot number: {SlotNumber}", slotNumber);
                    Ch.OpenCommPCI(slotNumber);
                    break;
                case "Simulator":
                    _logger.Debug("Opening simulator communication");
                    Ch.OpenCommSimulator();
                    break;
                default:
                    throw new ArgumentException("Invalid communication type");
            }

            DoMonitorEvent.Set();
            bConnected = true;
            ConnectionStatusChanged?.Invoke(bConnected);

            _logger.Information("Successfully connected to ACS controller");

            for (int axis = 0; axis < 3; axis++)
            {
                var (position, isEnabled, isMoving) = GetAxisStatus(axis);
                MotorStateChanged?.Invoke(axis, position, isEnabled, isMoving);
                _logger.Debug("Axis {Axis} status: Position={Position}, Enabled={Enabled}, Moving={Moving}", axis, position, isEnabled, isMoving);
            }
        }
        catch (COMException ex)
        {
            _logger.Error(ex, "COM Exception occurred during connection");
            HandleException(ex);
        }
        catch (ACSException ex)
        {
            _logger.Error(ex, "ACS Exception occurred during connection");
            HandleException(ex);
        }
    }



    public double[] GetCurrentACSPosition()
    {
        //_logger.Debug("Getting current ACS position for all axes");
        double[] qpos = new double[3];

        for (int axis = 0; axis < 3; axis++)
        {
            var (position, isEnabled, isMoving) = GetAxisStatus(axis);
            qpos[axis] = position;
            //_logger.Debug("Axis {Axis} position: {Position}", axis, position);
        }

        return qpos;
    }
    public void Disconnect()
    {
        if (bConnected)
        {
            try
            {
                DoMonitorEvent.Reset();
                MotorStateThreadIdleEvent.WaitOne();
                Ch.CloseComm();
                bConnected = false;
                ConnectionStatusChanged?.Invoke(bConnected);
            }
            catch (COMException ex)
            {
                HandleException(ex);
            }
        }
    }




    public async void RefreshGuiMotorStatus()
    {
        _logger.Debug("Refreshing GUI motor status for all axes");
        for (int axis = 0; axis < 3; axis++)
        {
            var (position, isEnabled, isMoving) = GetAxisStatus(axis);
            MotorStateChanged?.Invoke(axis, position, isEnabled, isMoving);
            _logger.Debug("Updated GUI for Axis {Axis}: Position={Position}, Enabled={Enabled}, Moving={Moving}", axis, position, isEnabled, isMoving);
            await Task.Delay(150);
        }
    }

    public void EnableMotor()
    {
        _logger.Information("Enabling motor for Axis {AxisNum}", AxisNum);
        if (bConnected)
        {
            try
            {
                Ch.EnableAsync(Axis);
                MotorEnabled?.Invoke();

                var (position, isEnabled, isMoving) = GetAxisStatus(AxisNum);
                MotorStateChanged?.Invoke(AxisNum, position, isEnabled, isMoving);
                _logger.Information("Motor enabled for Axis {AxisNum}", AxisNum);
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM Exception occurred while enabling motor for Axis {AxisNum}", AxisNum);
                HandleException(ex);
            }
        }
        else
        {
            _logger.Warning("Attempted to enable motor when not connected");
        }
    }

    public void DisableMotor()
    {
        _logger.Information("Disabling motor for Axis {AxisNum}", AxisNum);
        if (bConnected)
        {
            try
            {
                Ch.DisableAsync(Axis);
                MotorDisabled?.Invoke();

                var (position, isEnabled, isMoving) = GetAxisStatus(AxisNum);
                MotorStateChanged?.Invoke(AxisNum, position, isEnabled, isMoving);
                _logger.Information("Motor disabled for Axis {AxisNum}", AxisNum);
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM Exception occurred while disabling motor for Axis {AxisNum}", AxisNum);
                HandleException(ex);
            }
        }
        else
        {
            _logger.Warning("Attempted to disable motor when not connected");
        }
    }



    public Axis SetCurrentAxis(int selectIndex)
    {
        _logger.Information("Setting current axis to {AxisNum}", selectIndex);
        AxisNum = selectIndex;
        Axis = (ACS.SPiiPlusNET.Axis)AxisNum;
        return Axis;
    }

    public void MoveMotor(double increment)
    {
        _logger.Information("Moving motor for Axis {AxisNum} by increment {Increment}", AxisNum, increment);
        if (bConnected)
        {
            try
            {
                Ch.ToPointAsync(MotionFlags.ACSC_AMF_RELATIVE, Axis, increment);
                _logger.Debug("Initiated relative move for Axis {AxisNum}", AxisNum);
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM Exception occurred while moving motor for Axis {AxisNum}", AxisNum);
                HandleException(ex);
            }
        }
        else
        {
            _logger.Warning("Attempted to move motor when not connected");
        }
    }

    public void MoveMotorToAbsolute(double position)
    {
        _logger.Information("Moving motor for Axis {AxisNum} to absolute position {Position}", AxisNum, position);
        if (bConnected)
        {
            try
            {
                Ch.ToPointAsync(0, Axis, position);
                _logger.Debug("Initiated absolute move for Axis {AxisNum}", AxisNum);
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM Exception occurred while moving motor to absolute position for Axis {AxisNum}", AxisNum);
                HandleException(ex);
            }
        }
        else
        {
            _logger.Warning("Attempted to move motor to absolute position when not connected");
        }
    }


    public void MoveMotorAxis(int axisID, double increment)
    {
        _logger.Information("Moving motor for Axis {AxisID} by increment {Increment}", axisID, increment);
        if (bConnected)
        {
            Axis = (ACS.SPiiPlusNET.Axis)axisID;

            try
            {
                Ch.ToPointAsync(MotionFlags.ACSC_AMF_RELATIVE, Axis, increment);
                _logger.Debug("Initiated relative move for Axis {AxisID}", axisID);
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM Exception occurred while moving motor for Axis {AxisID}", axisID);
                HandleException(ex);
            }
        }
        else
        {
            _logger.Warning("Attempted to move motor axis when not connected");
        }
    }



    public void StopMotor()
    {
        _logger.Information("Stopping motor for Axis {AxisNum}", AxisNum);
        if (bConnected)
        {
            try
            {
                Ch.KillAsync(Axis);
                _logger.Debug("Initiated stop command for Axis {AxisNum}", AxisNum);
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM Exception occurred while stopping motor for Axis {AxisNum}", AxisNum);
                HandleException(ex);
            }
        }
        else
        {
            _logger.Warning("Attempted to stop motor when not connected");
        }
    }

    public void StopAllMotors()
    {
        _logger.Information("Stopping all motors");
        for (int i = 0; i < 3; i++)
        {
            ACS.SPiiPlusNET.Axis specifyAxis = (ACS.SPiiPlusNET.Axis)i;
            StopMotor(i);
        }
        _logger.Debug("Stop commands sent to all motors");
    }

    public void StopMotor(int specifyAxisNum)
    {
        _logger.Information("Stopping motor for Axis {AxisNum}", specifyAxisNum);
        ACS.SPiiPlusNET.Axis specifyAxis = (ACS.SPiiPlusNET.Axis)specifyAxisNum;
        if (bConnected)
        {
            try
            {
                Ch.KillAsync(specifyAxis);
                _logger.Debug("Initiated stop command for Axis {AxisNum}", specifyAxisNum);
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM Exception occurred while stopping motor for Axis {AxisNum}", specifyAxisNum);
                HandleException(ex);
            }
        }
        else
        {
            _logger.Warning("Attempted to stop motor when not connected");
        }
    }

    public void ZeroFeedbackPosition()
    {
        _logger.Information("Zeroing feedback position for Axis {AxisNum}", AxisNum);
        if (bConnected)
        {
            try
            {
                Ch.SetFPositionAsync(Axis, 0);
                _logger.Debug("Feedback position zeroed for Axis {AxisNum}", AxisNum);
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM Exception occurred while zeroing feedback position for Axis {AxisNum}", AxisNum);
                HandleException(ex);
            }
        }
        else
        {
            _logger.Warning("Attempted to zero feedback position when not connected");
        }
    }
    public (double position, bool isEnabled, bool isMoving) GetAxisStatus(int axisNum)
    {
        //_logger.Debug("Getting axis status for Axis {AxisNum}", axisNum);
        double position = 0;
        bool isEnabled = false;
        bool isMoving = false;

        try
        {
            // Attempt to get position
            position = Ch.GetFPosition((ACS.SPiiPlusNET.Axis)axisNum);

            // Get motor state only if position was successfully retrieved
            MotorStates state = Ch.GetMotorState((ACS.SPiiPlusNET.Axis)axisNum);
            isEnabled = Convert.ToBoolean(state & MotorStates.ACSC_MST_ENABLE);
            isMoving = Convert.ToBoolean(state & MotorStates.ACSC_MST_MOVE);

            //_logger.Debug("Axis {AxisNum} status: Position={Position}, Enabled={Enabled}, Moving={Moving}",axisNum, position, isEnabled, isMoving);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error getting position for Axis {AxisNum}. Using default values.", axisNum);
            position = 0;  // Default position if error occurs

            // Try to at least get the motor state
            try
            {
                MotorStates state = Ch.GetMotorState((ACS.SPiiPlusNET.Axis)axisNum);
                isEnabled = Convert.ToBoolean(state & MotorStates.ACSC_MST_ENABLE);
                isMoving = Convert.ToBoolean(state & MotorStates.ACSC_MST_MOVE);
            }
            catch (Exception stateEx)
            {
                _logger.Error(stateEx, "Error getting motor state for Axis {AxisNum}. Using default values.", axisNum);
                isEnabled = false;
                isMoving = false;
            }
        }

        return (position, isEnabled, isMoving);
    }



    private void MotorStateThreadProc()
    {
        _logger.Information("Motor state monitoring thread started for Axis {AxisNum}", AxisNum);
        while (true)
        {
            DoMonitorEvent.WaitOne();
            MotorStateThreadIdleEvent.Reset();

            try
            {
                double feedbackPos = Ch.GetFPosition(Axis);
                MotorStates state = Ch.GetMotorState(Axis);
                bool isEnabled = Convert.ToBoolean(state & MotorStates.ACSC_MST_ENABLE);
                bool isMoving = Convert.ToBoolean(state & MotorStates.ACSC_MST_MOVE);

                if (feedbackPos != previousPosition || isEnabled != previousIsEnabled || isMoving != previousIsMoving)
                {
                    previousPosition = feedbackPos;
                    previousIsEnabled = isEnabled;
                    previousIsMoving = isMoving;

                    MotorStateChanged?.Invoke(AxisNum, feedbackPos, isEnabled, isMoving);

                    if (!isMoving)
                    {
                        AllAxesIdle?.Invoke();
                    }
                }
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM Exception occurred in MotorStateThreadProc for Axis {AxisNum}", AxisNum);
                HandleException(ex);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected exception occurred in MotorStateThreadProc for Axis {AxisNum}", AxisNum);
                HandleException(ex);
            }

            MotorStateThreadIdleEvent.Set();
            Thread.Sleep(100);
        }
    }

    private void HandleException(Exception ex)
    {
        string message = $"Error from {ex.Source}\n{ex.Message}\nHRESULT: {ex.HResult:X}";
        ErrorOccurred?.Invoke(message);
    }
}

