using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic;

public abstract class PhotonNetworkTransport : INetworkTransport, IConnectionCallbacks, IOnEventCallback, IInRoomCallbacks, IMatchmakingCallbacks
{ 
    protected bool IsBlockPackages;//simulate disconnected
    protected const string testRoom = "test";

    public virtual void Connect() {
        IsBlockPackages = false;
    }

    public virtual void Disconnect() {
        IsBlockPackages = true;
    }
    
    public PhotonNetworkTransport() {
        PhotonNetwork.AddCallbackTarget(this);
    }

    public virtual void Shutdown() {
        PhotonNetwork.RemoveCallbackTarget(this);

        if (PhotonNetwork.InRoom) {
            PhotonNetwork.LeaveRoom();
        }
    }

    public string GetConnectionDescription(int connectionId) {
        if(PhotonNetwork.IsConnected || PhotonNetwork.InRoom) {
            return "Not connected";
        } else {
            return $"Region : {PhotonNetwork.CloudRegion}, Room name : {PhotonNetwork.CurrentRoom.Name}, " +
                $"ConnectionId : {PhotonNetwork.LocalPlayer.ActorNumber}, IsMasterClient : {PhotonNetwork.IsMasterClient}";
        }
    }

    public bool NextEvent(ref TransportEvent e) {
        if(transportEvents.Count > 0) {
            e = transportEvents.Dequeue();
            return true;
        }
        return false;
    }

    public abstract void SendData(int connectionId, TransportEvent.Type type, byte[] data);

    public void Update() {
    }

    //Photon interface
    //========================================================
    public void OnConnected() {
    }

    public virtual void OnConnectedToMaster() {
        Console.Write("Connected to master");
    }

    public virtual void OnDisconnected(DisconnectCause cause) {
        Console.Write("Disconnected due to : " + cause.ToString());
    }

    public abstract void OnEvent(EventData photonEvent);

    public virtual void OnRegionListReceived(RegionHandler regionHandler) {
    }

    public virtual void OnCustomAuthenticationResponse(Dictionary<string, object> data) {
    }

    public virtual void OnCustomAuthenticationFailed(string debugMessage) {
    }

    public virtual void OnPlayerEnteredRoom(Player newPlayer) {
    }

    public virtual void OnPlayerLeftRoom(Player otherPlayer) {
    }

    public virtual void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) {
    }

    public virtual void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps) {
    }

    public virtual void OnMasterClientSwitched(Player newMasterClient) {
    }

    public virtual void OnFriendListUpdate(List<FriendInfo> friendList) {
    }

    public virtual void OnCreatedRoom() {
    }

    public virtual void OnCreateRoomFailed(short returnCode, string message) {
    }

    public virtual void OnJoinedRoom() {
        GameDebug.Log("Joined room : " + PhotonNetwork.CurrentRoom.Name);
    }

    public virtual void OnJoinRoomFailed(short returnCode, string message) {
    }

    public virtual void OnJoinRandomFailed(short returnCode, string message) {
    }

    public virtual void OnLeftRoom() {
    }

    //========================================================

    protected Queue<TransportEvent> transportEvents = new Queue<TransportEvent>(256);
}

public class ClientPhotonNetworkTransport : PhotonNetworkTransport
{
    public override void Connect() {
        base.Connect();
        //wait server to send a connect event to this player

        //this is for starting game directly , not through lobby->room
        if (!PhotonNetwork.IsConnected) {
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    public override void OnConnectedToMaster() {
        base.OnConnectedToMaster();

        //this is for starting game directly , not through lobby->room
        if (!PhotonNetwork.InRoom) {
            PhotonNetwork.JoinOrCreateRoom(testRoom, new RoomOptions(), TypedLobby.Default);
        }
    }

    public override void OnEvent(EventData photonEvent) {
        if (IsBlockPackages) return;

        TransportEvent transportEvent = new TransportEvent();
        if (Enum.IsDefined(typeof(TransportEvent.Type), photonEvent.Code)) {
            transportEvent.type = (TransportEvent.Type)photonEvent.Code;
            transportEvent.ConnectionId = PhotonNetwork.LocalPlayer.ActorNumber;
        } else {//we may also receive some photon internal events
            return;
        }

        if((TransportEvent.Type)photonEvent.Code == TransportEvent.Type.Data) {
            transportEvent.Data = (byte[])photonEvent.CustomData;
        }

        transportEvents.Enqueue(transportEvent);
    }

    public override void SendData(int connectionId, TransportEvent.Type type, byte[] data) {
        if (IsBlockPackages) return;

        RaiseEventOptions options = new RaiseEventOptions() {
            Receivers = ReceiverGroup.MasterClient
        };
        PhotonNetwork.RaiseEvent((byte)type, data, options, SendOptions.SendUnreliable);
    }
}

public class ServerPhotonNetworkTransport : PhotonNetworkTransport, IMatchmakingCallbacks
{
    public override void Connect() {
        base.Connect();

        //this is for starting game directly , not through lobby->room
        if (!PhotonNetwork.IsConnected) {
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    public override void OnConnectedToMaster() {
        base.OnConnectedToMaster();

        //this is for starting game directly , not through lobby->room
        if (!PhotonNetwork.InRoom) {
            PhotonNetwork.JoinOrCreateRoom(testRoom, new RoomOptions(), TypedLobby.Default);
        }
    }

    public override void OnEvent(EventData photonEvent) {
        if (IsBlockPackages) return;

        TransportEvent transportEvent = new TransportEvent();
        if (Enum.IsDefined(typeof(TransportEvent.Type), photonEvent.Code)) {
            transportEvent.type = (TransportEvent.Type)photonEvent.Code;
            transportEvent.ConnectionId = photonEvent.Sender;
        } else {//we may also receive some photon internal events
            return;
        }

        if ((TransportEvent.Type)photonEvent.Code == TransportEvent.Type.Data) {
            transportEvent.Data = (byte[])photonEvent.CustomData;
        }

        transportEvents.Enqueue(transportEvent);
    }

    public override void OnJoinedRoom() {
        base.OnJoinedRoom();

        if (!PhotonNetwork.IsMasterClient) {
            GameDebug.Log("Request to be the master client");
            PhotonNetwork.SetMasterClient(PhotonNetwork.LocalPlayer);
        }
    }

    public override void OnMasterClientSwitched(Player newMasterClient) {
        GameDebug.Assert(newMasterClient == PhotonNetwork.LocalPlayer);
        GameDebug.Log("Server is the master client now");
    }

    public override void OnPlayerEnteredRoom(Player newPlayer) {
        base.OnPlayerEnteredRoom(newPlayer);

        transportEvents.Enqueue(new TransportEvent(TransportEvent.Type.Connect, newPlayer.ActorNumber, null));
    }

    public override void OnPlayerLeftRoom(Player otherPlayer) {
        base.OnPlayerLeftRoom(otherPlayer);

        transportEvents.Enqueue(new TransportEvent(TransportEvent.Type.Disconnect, otherPlayer.ActorNumber, null));
    }

    public override void SendData(int connectionId, TransportEvent.Type type, byte[] data) {
        if (IsBlockPackages) return;

        RaiseEventOptions options = new RaiseEventOptions() {
            TargetActors = new int[] { connectionId }
        };
        PhotonNetwork.RaiseEvent((byte)type, data, options, SendOptions.SendUnreliable);
    }
}

