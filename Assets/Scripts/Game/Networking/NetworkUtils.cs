using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

public static class NetworkUtils
{
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
}