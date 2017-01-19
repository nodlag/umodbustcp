using System;
using System.Collections.Generic;

public static class UModbusTCPHelpers {

    public static byte[] GetBytesOfInt(int _iValue) {
        //byte[] oResult = BitConverter.GetBytes(_iValue);
        byte[] oResult = new byte[2];
        oResult[1] = (byte)(_iValue >> 8);
        oResult[0] = (byte)_iValue;
        if(BitConverter.IsLittleEndian) {
            Array.Reverse(oResult);
        }
        return oResult;
    }

    public static byte[] GetBytesOfBool(bool _bValue) {
        if(!_bValue) {
            byte[] oResult = { 00, 00 };
            return oResult;
        } else {
            byte[] oResult = { 00, 01 };
            return oResult;
        }
    }

    public static int[] GetIntsOfBytes(byte[] _oArray) {
        if(BitConverter.IsLittleEndian) {
            Array.Reverse(_oArray);
        }
        List<int> oListResult = new List<int>();
        if(_oArray.Length < 2) {
            int iValue = _oArray[0];
            oListResult.Add(iValue);
        } else {
            for(int i = 0; i < _oArray.Length; i += 2) {
                int iValue = _oArray[i + 1] << 8 | _oArray[i];
                oListResult.Add(iValue);
            }
        }
        oListResult.Reverse();
        return oListResult.ToArray();
    }

}
