﻿public struct TransportEvent
{
    public enum Type
    {
        Data = 10,
        Connect,
        Disconnect
    }
    public Type type;
    public int ConnectionId;
    public byte[] Data;

    public TransportEvent(Type type, int connectionId, byte[] data) {
        this.type = type;
        this.ConnectionId = connectionId;
        this.Data = data;
    }
}

public interface INetworkTransport
{
    void Connect();
    void Disconnect();
    void Shutdown();
    bool NextEvent(ref TransportEvent e);
    void SendData(int connectionId, TransportEvent.Type type, byte[] data);
    string GetConnectionDescription(int connectionId);
    void Update();
}

public interface INetworkCallbacks
{
    void OnConnect(int clientId);
    void OnDisconnect(int clientId);
    //void OnEvent(int clientId, NetworkEvent info);
}

public static class NetworkConfig
{
    [ConfigVar(Name = "net.printstats", DefaultValue = "0", Description = "Print stats to console every N frame")]
    public static ConfigVar netPrintStats;

    public const string TestRoomName = "_test";
    public const int PhotonSendRate = 30;
    public const int PhotonSerializeRate = 30;

    public const int maxFixedSchemaIds = 2;
    public const int maxEventTypeSchemaIds = 8;
    public const int maxEntityTypeSchemaIds = 40;

    public const int maxSchemaIds = maxFixedSchemaIds + maxEventTypeSchemaIds + maxEntityTypeSchemaIds;

    public const int maxFieldsPerSchema = 128;
    public const int maxContextsPerField = 4;
    public const int maxSkipContextsPerSchema = maxFieldsPerSchema / 4;
    public const int maxContextsPerSchema = maxSkipContextsPerSchema + maxFieldsPerSchema * maxContextsPerField;

    // Number of serialized snapshots kept on server. Each server tick generate a snapshot. 
    public const int snapshotDeltaCacheSize = 128;  // Number of snapshots to cache for deltas

    // Size of client ack buffers. These buffers are used to keep track of ack'ed baselines
    // from clients. Theoretically the 'right' size is snapshotDeltaCacheSize / (server.tickrate / client.updaterate)
    // e.g. 128 / (60 / 20) = 128 / 3, but since client.updaterate <= server.tickrate we use
    public const int clientAckCacheSize = snapshotDeltaCacheSize;

    public const int firstSchemaContext = 16;
    public const int mapSchemaId = 1;

    public const int miscContext = 0;
    public const int baseSequenceContext = 1;
    public const int baseSequence1Context = 2;
    public const int baseSequence2Context = 3;
    public const int serverTimeContext = 4;

    public const int maxEventDataSize = 512;
    public const int maxCommandDataSize = 128;
    public const int maxEntitySnapshotDataSize = 512;
    public const int maxWorldSnapshotDataSize = 64 * 1024; // The entire world snapshot has to fit in this number of bytes
    public readonly static System.Text.UTF8Encoding encoding = new System.Text.UTF8Encoding();
    public readonly static float[] encoderPrecisionScales = new float[] { 1.0f, 10.0f, 100.0f, 1000.0f };
    public readonly static float[] decoderPrecisionScales = new float[] { 1.0f, 0.1f, 0.01f, 0.001f };
}

public enum NetworkMessage
{
    // Shared messages
    Events = 1 << 0,

    // Server -> Client messages
    ClientInfo = 1 << 1,
    MapInfo = 1 << 2,
    Snapshot = 1 << 3,

    // Client -> Server messages
    ClientConfig = 1 << 1,
    Commands = 1 << 2,
}