using System;
using UnityEngine;
using System.Collections;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// UModbusTCP implements a modbus TCP master driver for Unity.
/// Created by Javier Garrido Galdon @nodlag
/// Version 1.0.0
/// Writen using Hungarian Notation https://www.reactos.org/wiki/Hungarian_Notation
/// Based on 'Modbus TCP class' of Stephan Stricker https://www.codeproject.com/Tips/16260/Modbus-TCP-class
/// This class supports the following commands:
/// 
/// Read coils
/// Read discrete inputs
/// Write single coil
/// Write multiple cooils
/// Read holding register
/// Read input register
/// Write single register
/// Write multiple register
/// 
/// All commands can be sent in synchronous or asynchronous mode. If a value is accessed
/// in synchronous mode the program will stop and wait for slave to response. If the 
/// slave didn't answer within a specified time a timeout exception is called.
/// The class uses multi threading for both synchronous and asynchronous access. For
/// the communication two lines are created. This is necessary because the synchronous
/// thread has to wait for a previous command to finish.
/// 
/// </summary>
public class UModbusTCP : MonoBehaviour {

    //------------------------------------------------------------------------
    //Constants for access
    const byte FUNCTION_READ_COIL = 1;
    const byte FUNCTION_READ_DISCRETE_INPUTS = 2;
    const byte FUNCTION_READ_HOLDING_REGISTER = 3;
    const byte FUNCTION_READ_INPUT_REGISTER = 4;
    const byte FUNCTION_WRITE_SINGLE_COIL = 5;
    const byte FUNCTION_WRITE_SINGLE_REGISTER = 6;
    const byte FUNCTION_WRITE_MULTIPLE_COILS = 15;
    const byte FUNCTION_WRITE_MULTIPLE_REGISTER = 16;
    const byte FUNCTION_READ_WRITE_MULTIPLE_REGISTER = 23;

    /// <summary>Constant for exception illegal function.</summary>
    public const byte EXCEPTION_ILEGAL_FUNCTION = 1;
    /// <summary>Constant for exception illegal data address.</summary>
    public const byte EXCEPTION_ILEGAL_DATA_ADDRESS = 2;
    /// <summary>Constant for exception illegal data value.</summary>
    public const byte EXCEPTION_ILEGAL_DATA_VALUE = 3;
    /// <summary>Constant for exception slave device failure.</summary>
    public const byte EXCEPTION_SLAVE_DEVICA_FAILURE = 4;
    /// <summary>Constant for exception acknowledge.</summary>
    public const byte EXCEPTION_ACKNOWLEDGE = 5;
    /// <summary>Constant for exception slave is busy/booting up.</summary>
    public const byte EXCEPTION_SLAVE_IS_BUSY = 6;
    /// <summary>Constant for exception gate path unavailable.</summary>
    public const byte EXCEPTION_GATE_PATH_UNAVAILABLE = 10;
    /// <summary>Constant for exception not connected.</summary>
    public const byte EXCEPTION_NOT_CONNECTED = 253;
    /// <summary>Constant for exception connection lost.</summary>
    public const byte EXCEPTION_CONNECTION_LOST = 254;
    /// <summary>Constant for exception response timeout.</summary>
    public const byte EXCEPTION_TIMEOUT = 255;
    /// <summary>Constant for exception wrong offset.</summary>
    private const byte EXCEPTION_WRONG_OFFSET = 128;
    /// <summary>Constant for exception send fail.</summary>
    private const byte EXCEPTION_SEND_FAIL = 100;

    //------------------------------------------------------------------------
    //Unity singleton
    static UModbusTCP m_oInstance;

    public static UModbusTCP Instance {
        get {
            if(m_oInstance == null) {
                GameObject oGameObject = new GameObject();
                oGameObject.name = "UModbusTCP";
                m_oInstance = oGameObject.AddComponent<UModbusTCP>();
            }
            return m_oInstance;
        }
    }

    void Awake() {
        if(m_oInstance != null && m_oInstance != this) {
            Destroy(GetComponent<UModbusTCP>());
        } else {
            m_oInstance = this;
        }
    }

    //------------------------------------------------------------------------
    //Connection mode
    public enum CONNECTION_MODE {
        LINEAR,
        PARALLEL,
    }

    //------------------------------------------------------------------------
    //Private vars
    static string m_sIp;
    static ushort m_usPort;
    static ushort m_usConnectTimeout = 500;
    static ushort m_usTimeout = 500;
    static ushort m_usRefresh = 10;
    static bool m_bConnected = false;
    static bool m_bSyncConnected = false;
    static bool m_bAsyncConnected = false;

    //Sockets
    Socket m_oAsyncTCPSocket;
    byte[] m_bAsyncTCPSocketBuffer = new byte[2048];

    Socket m_oSyncTCPSocket;
    byte[] m_bSyncTCPSocketBuffer = new byte[2048];

    //------------------------------------------------------------------------
    /// <summary>Response data event. This event is called when new data arrives</summary>
    public delegate void ResponseData(ushort _usId, byte _bUnit, byte _bFunction, byte[] _bData);
    /// <summary>Response data event. This event is called when new data arrives</summary>
    public event ResponseData OnResponseData;
    /// <summary>Exception data event. This event is called when the data is incorrect</summary>
    public delegate void ExceptionData(ushort _usId, byte _bUnit, byte _bFunction, byte _bException);
    /// <summary>Exception data event. This event is called when the data is incorrect</summary>
    public event ExceptionData OnException;

    //------------------------------------------------------------------------
    /// <summary>Response timeout. If the slave didn't answers within in this time an exception is called.</summary>
    /// <value>The default value is 500ms.</value>
    public ushort timeout {
        get { return m_usTimeout; }
        set { m_usTimeout = value; }
    }

    //------------------------------------------------------------------------
    /// <summary>Connect timeout. If the slave didn't answers within in this time an exception is called.</summary>
    /// <value>The default value is 500ms.</value>
    public ushort connectTimeout {
        get { return m_usConnectTimeout; }
        set { m_usConnectTimeout = value; }
    }

    //------------------------------------------------------------------------
    /// <summary>Refresh timer for slave answer. The class is polling for answer every X ms.</summary>
    /// <value>The default value is 10ms.</value>
    public ushort refresh {
        get { return m_usRefresh; }
        set { m_usRefresh = value; }
    }

    //------------------------------------------------------------------------
    /// <summary>Shows if a connection is active.</summary>
    public bool connected {
        get { return m_bConnected; }
    }

    //------------------------------------------------------------------------
    /// <summary>Shows ip of connection activ.</summary>
    public string ip {
        get { return m_sIp; }
    }

    //------------------------------------------------------------------------
    /// <summary>Shows port of connection active.</summary>
    public ushort port {
        get { return m_usPort; }
    }


    //------------------------------------------------------------------------
    /// <summary>Create master instance without parameters.</summary>
    public UModbusTCP() {

    }

    //------------------------------------------------------------------------
    /// <summary>Create master instance with parameters.</summary>
    /// <param name="_sIp">IP adress of modbus slave.</param>
    /// <param name="_usPort">Port number of modbus slave. Usually port 502 is used.</param>
    public UModbusTCP(string _sIp, ushort _usPort) {
        m_sIp = _sIp;
        m_usPort = _usPort;
        Connect(_sIp, _usPort);
    }

    //------------------------------------------------------------------------
    /// <summary>Start connection to slave.</summary>
    /// <param name="_sIp">IP adress of modbus slave.</param>
    /// <param name="_usPort">Port number of modbus slave. Usually port 502 is used.</param>
    public void ConnectWithCoroutine(string _sIp, ushort _usPort, CONNECTION_MODE _eConnectionMode = CONNECTION_MODE.LINEAR) {
        m_sIp = _sIp;
        m_usPort = _usPort;
        StartCoroutine(ConnectCoroutine(_sIp, _usPort, _eConnectionMode));
    }

    public void StopConnectionCoroutine() {
        StopCoroutine("ConnectCoroutine");
        m_sIp = "0.0.0.0";
        m_usPort = 502;
        m_bConnected = false;
        m_bSyncConnected = false;
        m_bAsyncConnected = false;
    }

    //------------------------------------------------------------------------
    /// <summary>Start connection to slave.</summary>
    /// <param name="_sIp">IP adress of modbus slave.</param>
    /// <param name="_usPort">Port number of modbus slave. Usually port 502 is used.</param>
    public void Connect(string _sIp, ushort _usPort, CONNECTION_MODE _eConnectionMode = CONNECTION_MODE.LINEAR) {
        m_sIp = _sIp;
        m_usPort = _usPort;
        try {
            IPAddress oIp;
            if(IPAddress.TryParse(_sIp, out oIp) == false) {
                IPHostEntry hst = Dns.GetHostEntry(_sIp);
                _sIp = hst.AddressList[0].ToString();
            }
            // ----------------------------------------------------------------
            // Connect asynchronous client
            m_oAsyncTCPSocket = new Socket(IPAddress.Parse(_sIp).AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            m_oAsyncTCPSocket.ReceiveTimeout = m_usConnectTimeout;
            m_oAsyncTCPSocket.SendTimeout = m_usConnectTimeout;
            switch(_eConnectionMode) {
                case CONNECTION_MODE.PARALLEL:
                    m_oAsyncTCPSocket.BeginConnect(new IPEndPoint(IPAddress.Parse(_sIp), _usPort), new AsyncCallback(ConnectCoroutineCallback), null);
                    break;
                case CONNECTION_MODE.LINEAR:
                default:
                    m_oAsyncTCPSocket.Connect(new IPEndPoint(IPAddress.Parse(_sIp), _usPort));
                    break;
            }
#if !UNITY_ANDROID
            m_oAsyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, m_usTimeout);
            m_oAsyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, m_usTimeout);
            m_oAsyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, 1);
#endif
            // ----------------------------------------------------------------
            // Connect synchronous client
            m_oSyncTCPSocket = new Socket(IPAddress.Parse(_sIp).AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            m_oSyncTCPSocket.ReceiveTimeout = m_usConnectTimeout;
            m_oSyncTCPSocket.SendTimeout = m_usConnectTimeout;
            switch(_eConnectionMode) {
                case CONNECTION_MODE.PARALLEL:
                    m_oSyncTCPSocket.BeginConnect(new IPEndPoint(IPAddress.Parse(_sIp), _usPort), new AsyncCallback(ConnectCoroutineCallback), null);
                    break;
                case CONNECTION_MODE.LINEAR:
                default:
                    m_oSyncTCPSocket.Connect(new IPEndPoint(IPAddress.Parse(_sIp), _usPort));
                    break;
            }
#if !UNITY_ANDROID
            m_oSyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, m_usTimeout);
            m_oSyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, m_usTimeout);
            m_oSyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, 1);
#endif
            if(_eConnectionMode == CONNECTION_MODE.LINEAR) {
                m_bConnected = true;
            }
        } catch(System.IO.IOException _oError) {
            m_bConnected = false;
            m_bSyncConnected = false;
            m_bAsyncConnected = false;
            throw (_oError);
        }
    }

    public IEnumerator ConnectCoroutine(string _sIp, ushort _usPort, CONNECTION_MODE _eConnectionMode = CONNECTION_MODE.LINEAR) {
        m_sIp = _sIp;
        m_usPort = _usPort;
        try {
            m_bConnected = false;
            m_bSyncConnected = false;
            m_bAsyncConnected = false;
            IPAddress oIp;
            if(IPAddress.TryParse(_sIp, out oIp) == false) {
                IPHostEntry hst = Dns.GetHostEntry(_sIp);
                _sIp = hst.AddressList[0].ToString();
            }
            //----------------------------------------------------------------
            //Connect asynchronous client
            m_oAsyncTCPSocket = new Socket(IPAddress.Parse(_sIp).AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            m_oAsyncTCPSocket.ReceiveTimeout = m_usConnectTimeout;
            m_oAsyncTCPSocket.SendTimeout = m_usConnectTimeout;
            switch(_eConnectionMode) {
                case CONNECTION_MODE.PARALLEL:
                    m_oAsyncTCPSocket.BeginConnect(new IPEndPoint(IPAddress.Parse(_sIp), _usPort), new AsyncCallback(ConnectCoroutineCallback), null);
                    break;
                case CONNECTION_MODE.LINEAR:
                default:
                    m_oAsyncTCPSocket.Connect(new IPEndPoint(IPAddress.Parse(_sIp), _usPort));
                    break;
            }
#if !UNITY_ANDROID
            m_oAsyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, m_usTimeout);
            m_oAsyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, m_usTimeout);
            m_oAsyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, 1);
#endif
            //----------------------------------------------------------------
            //Connect synchronous client
            m_oSyncTCPSocket = new Socket(IPAddress.Parse(_sIp).AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            m_oSyncTCPSocket.ReceiveTimeout = m_usConnectTimeout;
            m_oSyncTCPSocket.SendTimeout = m_usConnectTimeout;
            switch(_eConnectionMode) {
                case CONNECTION_MODE.PARALLEL:
                    m_oSyncTCPSocket.BeginConnect(new IPEndPoint(IPAddress.Parse(_sIp), _usPort), new AsyncCallback(ConnectCoroutineCallback), null);
                    break;
                case CONNECTION_MODE.LINEAR:
                default:
                    m_oSyncTCPSocket.Connect(new IPEndPoint(IPAddress.Parse(_sIp), _usPort));
                    break;
            }
#if !UNITY_ANDROID
            m_oSyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, m_usTimeout);
            m_oSyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, m_usTimeout);
            m_oSyncTCPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, 1);
#endif
            if(_eConnectionMode == CONNECTION_MODE.LINEAR) {
                m_bConnected = true;
            }
        } catch {
            m_bConnected = false;
            m_bSyncConnected = false;
            m_bAsyncConnected = false;
            Debug.Log("Error connection on ConnectCoroutine");
        }
        yield break;
    }

    void ConnectCoroutineCallback(IAsyncResult _oResult) {
        m_bAsyncConnected = m_oAsyncTCPSocket.Connected;
        m_bSyncConnected = m_oSyncTCPSocket.Connected;
        if(m_bAsyncConnected && m_bSyncConnected) {
            m_bConnected = true;
        } else {
            m_bConnected = false;
        }
    }

    //------------------------------------------------------------------------
    /// <summary>Stop connection to slave.</summary>
    public void Disconnect() {
        Dispose();
    }

    //------------------------------------------------------------------------
    /// <summary>Destroy master instance.</summary>
    ~UModbusTCP() {
        Dispose();
    }

    //------------------------------------------------------------------------
    /// <summary>Destroy master instance</summary>
    public void Dispose() {
        m_sIp = "0.0.0.0";
        m_usPort = 502;
        m_bConnected = false;
        m_bSyncConnected = false;
        m_bAsyncConnected = false;
        if(m_oAsyncTCPSocket != null) {
            if(m_oAsyncTCPSocket.Connected) {
                try { m_oAsyncTCPSocket.Shutdown(SocketShutdown.Both); } catch { }
                m_oAsyncTCPSocket.Close();
            }
            m_oAsyncTCPSocket = null;
        }
        if(m_oSyncTCPSocket != null) {
            if(m_oSyncTCPSocket.Connected) {
                try { m_oSyncTCPSocket.Shutdown(SocketShutdown.Both); } catch { }
                m_oSyncTCPSocket.Close();
            }
            m_oSyncTCPSocket = null;
        }
    }

    internal void CallException(ushort _usId, byte _bUnit, byte _bFunction, byte _bException) {
        if((m_oAsyncTCPSocket == null) || (m_oSyncTCPSocket == null)) {
            return;
        }
        if(_bException == EXCEPTION_CONNECTION_LOST) {
            m_oSyncTCPSocket = null;
            m_oAsyncTCPSocket = null;
        }
        if(OnException != null) {
            OnException(_usId, _bUnit, _bFunction, _bException);
        }
    }

    internal static UInt16 SwapUInt16(UInt16 inValue) {
        return (UInt16)(((inValue & 0xff00) >> 8) |
                 ((inValue & 0x00ff) << 8));
    }

    //------------------------------------------------------------------------
    /// <summary>Read coils from slave asynchronous. The result is given in the response function.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumInputs">Length of data.</param>
    public void ReadCoils(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumInputs) {
        WriteAsyncData(CreateReadHeader(_usId, _bUnit, _usStartAddress, _usNumInputs, FUNCTION_READ_COIL), _usId);
    }

    //------------------------------------------------------------------------
    /// <summary>Read coils from slave synchronous.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumInputs">Length of data.</param>
    /// <param name="_bValues">Contains the result of function.</param>
    public void ReadCoils(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumInputs, ref byte[] _bValues) {
        _bValues = WriteSyncData(CreateReadHeader(_usId, _bUnit, _usStartAddress, _usNumInputs, FUNCTION_READ_COIL), _usId);
    }

    //------------------------------------------------------------------------
    /// <summary>Read discrete inputs from slave asynchronous. The result is given in the response function.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumInputs">Length of data.</param>
    public void ReadDiscreteInputs(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumInputs) {
        WriteAsyncData(CreateReadHeader(_usId, _bUnit, _usStartAddress, _usNumInputs, FUNCTION_READ_DISCRETE_INPUTS), _usId);
    }

    //------------------------------------------------------------------------
    /// <summary>Read discrete inputs from slave synchronous.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumInputs">Length of data.</param>
    /// <param name="_bValues">Contains the result of function.</param>
    public void ReadDiscreteInputs(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumInputs, ref byte[] _bValues) {
        _bValues = WriteSyncData(CreateReadHeader(_usId, _bUnit, _usStartAddress, _usNumInputs, FUNCTION_READ_DISCRETE_INPUTS), _usId);
    }

    //------------------------------------------------------------------------
    /// <summary>Read holding registers from slave asynchronous. The result is given in the response function.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumInputs">Length of data.</param>
    public byte[] ReadHoldingRegister(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumInputs) {
        byte[] bHeader = CreateReadHeader(_usId, _bUnit, _usStartAddress, _usNumInputs, FUNCTION_READ_HOLDING_REGISTER);
        WriteAsyncData(bHeader, _usId);
        return bHeader;
    }

    //------------------------------------------------------------------------
    /// <summary>Read holding registers from slave synchronous.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumInputs">Length of data.</param>
    /// <param name="_bValues">Contains the result of function.</param>
    public void ReadHoldingRegister(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumInputs, ref byte[] _bValues) {
        _bValues = WriteSyncData(CreateReadHeader(_usId, _bUnit, _usStartAddress, _usNumInputs, FUNCTION_READ_HOLDING_REGISTER), _usId);
    }

    //------------------------------------------------------------------------
    /// <summary>Read input registers from slave asynchronous. The result is given in the response function.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumInputs">Length of data.</param>
    public void ReadInputRegister(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumInputs) {
        WriteAsyncData(CreateReadHeader(_usId, _bUnit, _usStartAddress, _usNumInputs, FUNCTION_READ_INPUT_REGISTER), _usId);
    }

    //------------------------------------------------------------------------
    /// <summary>Read input registers from slave synchronous.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumInputs">Length of data.</param>
    /// <param name="_bValues">Contains the result of function.</param>
    public void ReadInputRegister(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumInputs, ref byte[] _bValues) {
        _bValues = WriteSyncData(CreateReadHeader(_usId, _bUnit, _usStartAddress, _usNumInputs, FUNCTION_READ_INPUT_REGISTER), _usId);
    }

    //------------------------------------------------------------------------
    /// <summary>Write single coil in slave asynchronous. The result is given in the response function.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_bOnOff">Specifys if the coil should be switched on or off.</param>
    public byte[] WriteSingleCoils(ushort _usId, byte _bUnit, ushort _usStartAddress, bool _bOnOff) {
        byte[] bData;
        bData = CreateWriteHeader(_usId, _bUnit, _usStartAddress, 1, 1, FUNCTION_WRITE_SINGLE_COIL);
        if(_bOnOff == true) {
            bData[10] = 255;
        } else {
            bData[10] = 0;
        }
        WriteAsyncData(bData, _usId);
        return bData;
    }

    //------------------------------------------------------------------------
    /// <summary>Write single coil in slave synchronous.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_bOnOff">Specifys if the coil should be switched on or off.</param>
    /// <param name="_bResult">Contains the result of the synchronous write.</param>
    public void WriteSingleCoils(ushort _usId, byte _bUnit, ushort _usStartAddress, bool _bOnOff, ref byte[] _bResult) {
        byte[] bData;
        bData = CreateWriteHeader(_usId, _bUnit, _usStartAddress, 1, 1, FUNCTION_WRITE_SINGLE_COIL);
        if(_bOnOff == true) {
            bData[10] = 255;
        } else {
            bData[10] = 0;
        }
        _bResult = WriteSyncData(bData, _usId);
    }

    //------------------------------------------------------------------------
    /// <summary>Write multiple coils in slave asynchronous. The result is given in the response function.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumBits">Specifys number of bits.</param>
    /// <param name="_bValues">Contains the bit information in byte format.</param>
    public void WriteMultipleCoils(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumBits, byte[] _bValues) {
        byte bNumBytes = Convert.ToByte(_bValues.Length);
        byte[] bData;
        bData = CreateWriteHeader(_usId, _bUnit, _usStartAddress, _usNumBits, (byte)(bNumBytes + 2), FUNCTION_WRITE_MULTIPLE_COILS);
        Array.Copy(_bValues, 0, bData, 13, bNumBytes);
        WriteAsyncData(bData, _usId);
    }

    //------------------------------------------------------------------------
    /// <summary>Write multiple coils in slave synchronous.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="numBits">Specifys number of bits.</param>
    /// <param name="_bValues">Contains the bit information in byte format.</param>
    /// <param name="_bResult">Contains the result of the synchronous write.</param>
    public void WriteMultipleCoils(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort numBits, byte[] _bValues, ref byte[] _bResult) {
        byte bNumBytes = Convert.ToByte(_bValues.Length);
        byte[] bData;
        bData = CreateWriteHeader(_usId, _bUnit, _usStartAddress, numBits, (byte)(bNumBytes + 2), FUNCTION_WRITE_MULTIPLE_COILS);
        Array.Copy(_bValues, 0, bData, 13, bNumBytes);
        _bResult = WriteSyncData(bData, _usId);
    }

    //------------------------------------------------------------------------
    /// <summary>Write single register in slave asynchronous. The result is given in the response function.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address to where the data is written.</param>
    /// <param name="_bValues">Contains the register information.</param>
    public byte[] WriteSingleRegister(ushort _usId, byte _bUnit, ushort _usStartAddress, byte[] _bValues) {
        byte[] bData;
        bData = CreateWriteHeader(_usId, _bUnit, _usStartAddress, 1, 1, FUNCTION_WRITE_SINGLE_REGISTER);
        bData[10] = _bValues[0];
        bData[11] = _bValues[1];
        WriteAsyncData(bData, _usId);
        return bData;
    }

    //------------------------------------------------------------------------
    /// <summary>Write single register in slave synchronous.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address to where the data is written.</param>
    /// <param name="_bValues">Contains the register information.</param>
    /// <param name="_bResult">Contains the result of the synchronous write.</param>
    public byte[] WriteSingleRegister(ushort _usId, byte _bUnit, ushort _usStartAddress, byte[] _bValues, ref byte[] _bResult) {
        byte[] bData;
        bData = CreateWriteHeader(_usId, _bUnit, _usStartAddress, 1, 1, FUNCTION_WRITE_SINGLE_REGISTER);
        bData[10] = _bValues[0];
        bData[11] = _bValues[1];
        _bResult = WriteSyncData(bData, _usId);
        return bData;
    }

    //------------------------------------------------------------------------
    /// <summary>Write multiple registers in slave asynchronous. The result is given in the response function.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address to where the data is written.</param>
    /// <param name="_bValues">Contains the register information.</param>
    public byte[] WriteMultipleRegister(ushort _usId, byte _bUnit, ushort _usStartAddress, byte[] _bValues) {
        ushort usNumBytes = Convert.ToUInt16(_bValues.Length);
        if(usNumBytes % 2 > 0) {
            usNumBytes++;
        }
        byte[] bData;

        bData = CreateWriteHeader(_usId, _bUnit, _usStartAddress, Convert.ToUInt16(usNumBytes / 2), Convert.ToUInt16(usNumBytes + 2), FUNCTION_WRITE_MULTIPLE_REGISTER);
        Array.Copy(_bValues, 0, bData, 13, _bValues.Length);
        WriteAsyncData(bData, _usId);
        return bData;
    }

    //------------------------------------------------------------------------
    /// <summary>Write multiple registers in slave synchronous.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address to where the data is written.</param>
    /// <param name="_bValues">Contains the register information.</param>
    /// <param name="_bResult">Contains the result of the synchronous write.</param>
    public byte[] WriteMultipleRegister(ushort _usId, byte _bUnit, ushort _usStartAddress, byte[] _bValues, ref byte[] _bResult) {
        ushort usNumBytes = Convert.ToUInt16(_bValues.Length);
        if(usNumBytes % 2 > 0) {
            usNumBytes++;
        }
        byte[] bData;

        bData = CreateWriteHeader(_usId, _bUnit, _usStartAddress, Convert.ToUInt16(usNumBytes / 2), Convert.ToUInt16(usNumBytes + 2), FUNCTION_WRITE_MULTIPLE_REGISTER);
        Array.Copy(_bValues, 0, bData, 13, _bValues.Length);
        _bResult = WriteSyncData(bData, _usId);
        return bData;
    }

    //------------------------------------------------------------------------
    /// <summary>Read/Write multiple registers in slave asynchronous. The result is given in the response function.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumInputs">Length of data.</param>
    /// <param name="_usStartWriteAddress">Address to where the data is written.</param>
    /// <param name="_bValues">Contains the register information.</param>
    public byte[] ReadWriteMultipleRegister(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumInputs, ushort _usStartWriteAddress, byte[] _bValues) {
        ushort usNumBytes = Convert.ToUInt16(_bValues.Length);
        if(usNumBytes % 2 > 0) {
            usNumBytes++;
        }
        byte[] bData;

        bData = CreateReadWriteHeader(_usId, _bUnit, _usStartAddress, _usNumInputs, _usStartWriteAddress, Convert.ToUInt16(usNumBytes / 2));
        Array.Copy(_bValues, 0, bData, 17, _bValues.Length);
        WriteAsyncData(bData, _usId);
        return bData;
    }

    //------------------------------------------------------------------------
    /// <summary>Read/Write multiple registers in slave synchronous. The result is given in the response function.</summary>
    /// <param name="_usId">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
    /// <param name="_bUnit">Unit identifier (previously slave address). In asynchonous mode this unit is given to the callback function.</param>
    /// <param name="_usStartAddress">Address from where the data read begins.</param>
    /// <param name="_usNumInputs">Length of data.</param>
    /// <param name="_usStartWriteAddress">Address to where the data is written.</param>
    /// <param name="_bValues">Contains the register information.</param>
    /// <param name="_bResult">Contains the result of the synchronous command.</param>
    public byte[] ReadWriteMultipleRegister(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumInputs, ushort _usStartWriteAddress, byte[] _bValues, ref byte[] _bResult) {
        ushort usNumBytes = Convert.ToUInt16(_bValues.Length);
        if(usNumBytes % 2 > 0) {
            usNumBytes++;
        }
        byte[] bData;

        bData = CreateReadWriteHeader(_usId, _bUnit, _usStartAddress, _usNumInputs, _usStartWriteAddress, Convert.ToUInt16(usNumBytes / 2));
        Array.Copy(_bValues, 0, bData, 17, _bValues.Length);
        _bResult = WriteSyncData(bData, _usId);
        return bData;
    }

    //------------------------------------------------------------------------
    //Create modbus header for read action
    private byte[] CreateReadHeader(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usLength, byte _bFunction) {
        byte[] bData = new byte[12];

        byte[] bId = BitConverter.GetBytes((short)_usId);
        bData[0] = bId[1];                  // Slave id high byte
        bData[1] = bId[0];                  // Slave id low byte
        bData[5] = 6;                       // Message size
        bData[6] = _bUnit;                  // Slave address
        bData[7] = _bFunction;              // Function code
        byte[] bAddress = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)_usStartAddress));
        bData[8] = bAddress[0];             // Start address
        bData[9] = bAddress[1];             // Start address
        byte[] bLength = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)_usLength));
        bData[10] = bLength[0];             // Number of data to read
        bData[11] = bLength[1];             // Number of data to read

        return bData;
    }

    //------------------------------------------------------------------------
    //Create modbus header for write action
    private byte[] CreateWriteHeader(ushort _usId, byte _bUnit, ushort _usStartAddress, ushort _usNumData, ushort _usNumBytes, byte _bFunction) {
        byte[] bData = new byte[_usNumBytes + 11];

        byte[] bId = BitConverter.GetBytes((short)_usId);
        bData[0] = bId[1];                      // Slave id high byte
        bData[1] = bId[0];                      // Slave id low byte
        byte[] bSize = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)(5 + _usNumBytes)));
        bData[4] = bSize[0];                    // Complete message size in bytes
        bData[5] = bSize[1];                    // Complete message size in bytes
        bData[6] = _bUnit;                      // Slave address
        bData[7] = _bFunction;                  // Function code
        byte[] bAddress = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)_usStartAddress));
        bData[8] = bAddress[0];                 // Start address
        bData[9] = bAddress[1];                 // Start address
        if(_bFunction >= FUNCTION_WRITE_MULTIPLE_COILS) {
            byte[] bCnt = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)_usNumData));
            bData[10] = bCnt[0];                // Number of bytes
            bData[11] = bCnt[1];                // Number of bytes
            bData[12] = (byte)(_usNumBytes - 2);
        }

        return bData;
    }

    //------------------------------------------------------------------------
    //Create modbus header for read/write action
    private byte[] CreateReadWriteHeader(ushort _usId, byte _bUnit, ushort _usStartReadAddress, ushort _usNumRead, ushort _usStartWriteAddress, ushort _usNumWrite) {
        byte[] bData = new byte[_usNumWrite * 2 + 17];

        byte[] bId = BitConverter.GetBytes((short)_usId);
        bData[0] = bId[1];                       // Slave id high byte
        bData[1] = bId[0];                       // Slave id low byte
        byte[] bSize = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)(11 + _usNumWrite * 2)));
        bData[4] = bSize[0];                     // Complete message size in bytes
        bData[5] = bSize[1];                     // Complete message size in bytes
        bData[6] = _bUnit;                       // Slave address
        bData[7] = FUNCTION_READ_WRITE_MULTIPLE_REGISTER; // Function code
        byte[] bAddressRead = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)_usStartReadAddress));
        bData[8] = bAddressRead[0];              // Start read address
        bData[9] = bAddressRead[1];              // Start read address
        byte[] bCntRead = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)_usNumRead));
        bData[10] = bCntRead[0];                 // Number of bytes to read
        bData[11] = bCntRead[1];                 // Number of bytes to read
        byte[] bAddressWrite = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)_usStartWriteAddress));
        bData[12] = bAddressWrite[0];            // Start write address
        bData[13] = bAddressWrite[1];            // Start write address
        byte[] bCntWrite = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)_usNumWrite));
        bData[14] = bCntWrite[0];                // Number of bytes to write
        bData[15] = bCntWrite[1];                // Number of bytes to write
        bData[16] = (byte)(_usNumWrite * 2);

        return bData;
    }

    //------------------------------------------------------------------------
    //Write asynchronous data
    private void WriteAsyncData(byte[] _bWriteData, ushort _usId) {
        if((m_oAsyncTCPSocket != null) && (m_oAsyncTCPSocket.Connected)) {
            try {
                m_oAsyncTCPSocket.BeginSend(_bWriteData, 0, _bWriteData.Length, SocketFlags.None, new AsyncCallback(OnSend), null);
                m_oAsyncTCPSocket.BeginReceive(m_bAsyncTCPSocketBuffer, 0, m_bAsyncTCPSocketBuffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), m_oAsyncTCPSocket);
            } catch(SystemException) {
                CallException(_usId, _bWriteData[6], _bWriteData[7], EXCEPTION_CONNECTION_LOST);
            }
        } else {
            CallException(_usId, _bWriteData[6], _bWriteData[7], EXCEPTION_CONNECTION_LOST);
        }
    }

    //------------------------------------------------------------------------
    //Write asynchronous data acknowledge
    private void OnSend(System.IAsyncResult _oResult) {
        if(_oResult.IsCompleted == false) {
            CallException(0xFFFF, 0xFF, 0xFF, EXCEPTION_SEND_FAIL);
        }
    }

    //------------------------------------------------------------------------
    //Write asynchronous data response
    private void OnReceive(System.IAsyncResult _oResult) {
        if(_oResult.IsCompleted == false) {
            CallException(0xFF, 0xFF, 0xFF, EXCEPTION_CONNECTION_LOST);
        }
        ushort usId = SwapUInt16(BitConverter.ToUInt16(m_bAsyncTCPSocketBuffer, 0));
        byte bUnit = m_bAsyncTCPSocketBuffer[6];
        byte bFunction = m_bAsyncTCPSocketBuffer[7];
        byte[] bData;

        //------------------------------------------------------------
        //Write response data
        if((bFunction >= FUNCTION_WRITE_SINGLE_COIL) && (bFunction != FUNCTION_READ_WRITE_MULTIPLE_REGISTER)) {
            bData = new byte[2];
            Array.Copy(m_bAsyncTCPSocketBuffer, 10, bData, 0, 2);
        } else {
            //------------------------------------------------------------
            //Read response data
            bData = new byte[m_bAsyncTCPSocketBuffer[8]];
            Array.Copy(m_bAsyncTCPSocketBuffer, 9, bData, 0, m_bAsyncTCPSocketBuffer[8]);
        }
        //------------------------------------------------------------
        //Response data is slave exception
        if(bFunction > EXCEPTION_WRONG_OFFSET) {
            bFunction -= EXCEPTION_WRONG_OFFSET;
            CallException(usId, bUnit, bFunction, m_bAsyncTCPSocketBuffer[8]);
        } else if(OnResponseData != null) {
            //------------------------------------------------------------
            //Response data is regular data
            OnResponseData(usId, bUnit, bFunction, m_bAsyncTCPSocketBuffer);
        }
    }

    //------------------------------------------------------------------------
    //Write data and and wait for response
    private byte[] WriteSyncData(byte[] _bWriteData, ushort _uId) {
        if(m_oSyncTCPSocket.Connected) {
            try {
                m_oSyncTCPSocket.Send(_bWriteData, 0, _bWriteData.Length, SocketFlags.None);
                int iResult = m_oSyncTCPSocket.Receive(m_bSyncTCPSocketBuffer, 0, m_bSyncTCPSocketBuffer.Length, SocketFlags.None);

                byte bUnit = m_bSyncTCPSocketBuffer[6];
                byte bFunction = m_bSyncTCPSocketBuffer[7];
                byte[] bData;

                if(iResult == 0) {
                    CallException(_uId, bUnit, _bWriteData[7], EXCEPTION_CONNECTION_LOST);
                }

                //------------------------------------------------------------
                //Response data is slave exception
                if(bFunction > EXCEPTION_WRONG_OFFSET) {
                    bFunction -= EXCEPTION_WRONG_OFFSET;
                    CallException(_uId, bUnit, bFunction, m_bSyncTCPSocketBuffer[8]);
                    return null;
                } else if((bFunction >= FUNCTION_WRITE_SINGLE_COIL) && (bFunction != FUNCTION_READ_WRITE_MULTIPLE_REGISTER)) {
                    //------------------------------------------------------------
                    //Write response data
                    bData = new byte[2];
                    Array.Copy(m_bSyncTCPSocketBuffer, 10, bData, 0, 2);
                } else {
                    //------------------------------------------------------------
                    //Read response data
                    bData = new byte[m_bSyncTCPSocketBuffer[8]];
                    Array.Copy(m_bSyncTCPSocketBuffer, 9, bData, 0, m_bSyncTCPSocketBuffer[8]);
                }
                return bData;
            } catch(SystemException) {
                CallException(_uId, _bWriteData[6], _bWriteData[7], EXCEPTION_CONNECTION_LOST);
            }
        } else {
            CallException(_uId, _bWriteData[6], _bWriteData[7], EXCEPTION_CONNECTION_LOST);
        }
        return null;
    }
}

