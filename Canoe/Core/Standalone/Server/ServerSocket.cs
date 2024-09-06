#if !UNITY_WEBGL || UNITY_EDITOR
using FishNet.Connection;
using FishNet.Managing;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.WebRTC;


namespace FishNet.Transporting.CanoeWebRTC.Server
{
    public class ServerSocket : CommonSocket
    {
        public int nextConnID = 0;
        public Dictionary<int, Connection> connections = new Dictionary<int, Connection>();

        private ConcurrentQueue<LocalConnectionState> _localConnectionStates = new ConcurrentQueue<LocalConnectionState>();
        private ConcurrentQueue<RemoteConnectionEvent> _remoteConnectionEvents = new ConcurrentQueue<RemoteConnectionEvent>();

        public RemoteConnectionState GetConnectionState(int connectionId)
        {
            Connection connection = connections[connectionId];

            if (connection.localConnection == null || connection.localConnection.ConnectionState != RTCPeerConnectionState.Connected)
            {
                return RemoteConnectionState.Stopped;
            }
            else
            {
                return RemoteConnectionState.Started;
            }
        }



        public bool StartServer(Transport t, int mtu)
        {

            if (base.GetLocalConnectionState() != LocalConnectionState.Stopped)
                return false;

            base.t = t;
            base.mtu = mtu;


            if (base.GetLocalConnectionState() != LocalConnectionState.Stopped)
            {
                //we are already starting / started
                return false;
            }

            ResetQueues();

            _localConnectionStates.Enqueue(LocalConnectionState.Started);


            return true;

        }

        public bool StopServer()
        {

            if (base.GetLocalConnectionState() == LocalConnectionState.Stopped || base.GetLocalConnectionState() == LocalConnectionState.Stopping)
                return false;

            int[] IDs = connections.Keys.ToArray();
            foreach (int ID in IDs)
            {
                CloseConnection(ID);
            }

            connections.Clear();
            nextConnID = 0;

            base.SetConnectionState(LocalConnectionState.Stopping, true);
            base.SetConnectionState(LocalConnectionState.Stopped, true);


            return true;

        }

        public int CreateConnection()
        {
            int connectionID = nextConnID;
            Connection newConnection = new Connection(null, this, connectionID);
            connections.Add(connectionID, newConnection);
            nextConnID = (nextConnID + 1) & 0x7FFFFFFF; // wrap to 0 after reaching int.MaxValue
            return connectionID;
        }


        public bool CloseConnection(int connectionID)
        {
            if (base.GetLocalConnectionState() != LocalConnectionState.Started)
                return false;

            InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Close Connection on <b><i><color=#DDA0DD>{connectionID}</color></i></b>");
            connections[connectionID].CloseAll();
            connections.Remove(connectionID);
            UpdateRemoteConnectionState(RemoteConnectionState.Stopped, connectionID);
            return true;
        }

        public void UpdateRemoteConnectionState(RemoteConnectionState newState, int connID)
        {
            _remoteConnectionEvents.Enqueue(new RemoteConnectionEvent(newState == RemoteConnectionState.Started ? true : false, connID));

        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IterateOutgoing()
        {
            if (base.GetLocalConnectionState() != LocalConnectionState.Started)
            {
                //Debug.Log($"<color=#FFA500>[Server]</color> Tried to iterate outgoing while server was not opened");
                base.ClearPacketQueue(ref base._outgoing);
            }
            else
            {
                int count = base._outgoing.Count;
                for (int i = 0; i < count; i++)
                {
                    Packet outgoing = base._outgoing.Dequeue();

                    int connectionID = outgoing.ConnectionID;
                    byte[] data = outgoing.Data;
                    byte channelID = outgoing.Channel;


                    //If over the MTU.
                    if (outgoing.Channel == (byte)Channel.Unreliable && data.Length > base.mtu)
                    {
                        base.t.NetworkManager.LogWarning($"<color=#FFA500>[Server]</color> is sending of {data.Length} length on the unreliable channel, while the MTU is only {base.mtu}. The channel has been changed to reliable for this send.");
                        channelID = (byte)Channel.Reliable;
                    }


                    if (connectionID == NetworkConnection.UNSET_CLIENTID_VALUE)
                    {
                        SendToAll(channelID, data);
                    }
                    else
                    {
                        SendToOne(connectionID, channelID, data);
                    }

                    outgoing.Dispose();

                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IterateIncoming()
        {

            while (_localConnectionStates.TryDequeue(out LocalConnectionState result))
            {

                InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Local Connection State set to <b><i><color=#DDA0DD>{result}</color></i></b>");
                base.SetConnectionState(result, true);
            }
            //Not yet started.
            LocalConnectionState localState = base.GetLocalConnectionState();
            if (localState != LocalConnectionState.Started)
            {
                ResetQueues();
                //If stopped try to kill task.
                if (localState == LocalConnectionState.Stopped)
                {
                    StopServer();
                    return;
                }
            }

            while (_remoteConnectionEvents.TryDequeue(out RemoteConnectionEvent connectionEvent))
            {

                InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Remote Connection State set on <b><i><color=#DDA0DD>{connectionEvent.ConnectionId}</color></i></b> to <b><i><color=#DDA0DD>{(connectionEvent.Connected ? "Started" : "Stopped")}</color></i></b>");


                base.t.HandleRemoteConnectionState(new RemoteConnectionStateArgs(connectionEvent.Connected ? RemoteConnectionState.Started : RemoteConnectionState.Stopped, connectionEvent.ConnectionId, base.t.Index));
            }

            //Handle packets.
            while (base._incoming.TryDequeue(out Packet incoming))
            {


                ServerReceivedDataArgs dataArgs = new ServerReceivedDataArgs(
                    incoming.GetArraySegment(),
                    (Channel)incoming.Channel,
                    incoming.ConnectionID,
                    base.t.Index);

                base.t.HandleServerReceivedDataArgs(dataArgs);


                incoming.Dispose();
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendToOne(int connID, byte channelID, byte[] data)
        {
            if (!connections.ContainsKey(connID))
            {
                base.t.NetworkManager.LogWarning($"<color=#FFA500>[Server]</color> Attempted to send message to non existant connection [{connID}]");
                return;
            }
            else
            {

                if (channelID == (byte)Channel.Reliable)
                {
                    connections[connID].reliableSends.Enqueue(data);
                    connections[connID].reliablePending.Set();
                }
                else
                {
                    connections[connID].unreliableSends.Enqueue(data);
                    connections[connID].unreliablePending.Set();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendToAll(byte channelID, byte[] data)
        {
            if (channelID == (byte)Channel.Reliable)
            {
                foreach (Connection connection in connections.Values)
                {
                    connection.reliableSends.Enqueue(data);
                    connection.reliablePending.Set();
                }
            }
            else
            {
                foreach (Connection connection in connections.Values)
                {
                    connection.unreliableSends.Enqueue(data);
                    connection.unreliablePending.Set();
                }
            }
        }

        private void ResetQueues()
        {
            base.ClearGenericQueue<LocalConnectionState>(ref _localConnectionStates);
            base.ClearPacketQueue(ref _incoming);
            base.ClearPacketQueue(ref _outgoing);
            base.ClearGenericQueue<RemoteConnectionEvent>(ref _remoteConnectionEvents);
        }


    }
}
#endif