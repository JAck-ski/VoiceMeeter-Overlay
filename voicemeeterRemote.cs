using System;
using System.Runtime.InteropServices;

public static class VoicemeeterRemote
{
    [DllImport("VoicemeeterRemote64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int VBVMR_Login();

    [DllImport("VoicemeeterRemote64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int VBVMR_Logout();

    // nType: 0 = input pre-fader, 1 = input post-fader, 2 = output
    [DllImport("VoicemeeterRemote64.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int VBVMR_GetLevel(int nType, int channel, out float value);

    // Get parameter (e.g. "Strip[5].Gain")
    [DllImport("VoicemeeterRemote64.dll",
        CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi)]
    public static extern int VBVMR_GetParameterFloat(string szParamName, out float value);
}
