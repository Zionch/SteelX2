using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

public static class NetworkUtils
{
    public static uint FloatToUInt32(float value) { return new UIntFloat() { floatValue = value }.intValue; }
    public static float UInt32ToFloat(uint value) { return new UIntFloat() { intValue = value }.floatValue; }

    static NetworkUtils() {
        stopwatch.Start();
    }

    public static System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

    public static void MemCopy(byte[] src, int srcIndex, byte[] dst, int dstIndex, int count) {
        for (int i = 0; i < count; ++i)
            dst[dstIndex++] = src[srcIndex++];
    }

    public static int MemCmp(byte[] a, int aIndex, byte[] b, int bIndex, int count) {
        for (int i = 0; i < count; ++i) {
            var diff = b[bIndex++] - a[aIndex++];
            if (diff != 0)
                return diff;
        }

        return 0;
    }
    public static int MemCmp(uint[] a, int aIndex, uint[] b, int bIndex, int count) {
        for (int i = 0; i < count; ++i) {
            var diff = b[bIndex++] - a[aIndex++];
            if (diff != 0)
                return (int)diff;
        }

        return 0;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct UIntFloat
    {
        [FieldOffset(0)]
        public float floatValue;
        [FieldOffset(0)]
        public uint intValue;
    }
}

public class Aggregator
{
    const int k_WindowSize = 120;

    public float previousValue;
    public FloatRollingAverage graph = new FloatRollingAverage(k_WindowSize);

    public void Update(float value) {
        graph.Update(value - previousValue);
        previousValue = value;
    }
}