using AOT;
using FishNet.Connection;
using FishNet.Managing;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;


namespace FishNet.Transporting.CanoeWebRTC.Server
{
    public class WebGLServerSocket : CommonSocket
    {
        [MonoPInvokeCallback(typeof(Action<int>))]
        private static void RemoteChannelClosedCallback_Server(int connectionID)
        {
            InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Remote channel closed for connection ID {connectionID}");
            Instance.CloseConnection(connectionID);
        }

        [MonoPInvokeCallback(typeof(Action<int>))]
        private static void RemoteChannelOpenedCallback_Server(int connectionID)
        {
            InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Remote channel opened for connection ID {connectionID}");
            //Debug.Log($"<color=#FFA500>[Server]</color> Remote channel opened for connection ID {connectionID}");
            UpdateRemoteConnectionState(RemoteConnectionState.Started, connectionID);
        }

        [MonoPInvokeCallback(typeof(Action<int, IntPtr, int>))]
        private static void ReliableMessageReceivedCallback_Server(int connectionID, IntPtr dataPtr, int dataSize)
        {
            //Debug.Log($"Server: Reliable message received for connection ID {connectionID}");

            if (dataSize > Instance.mtu)
            {
                Instance.CloseConnection(connectionID);
            }
            else
            {
                byte[] messageData = CustomByteArrayPool.Retrieve(dataSize);
                Marshal.Copy(dataPtr, messageData, 0, dataSize);
                Instance._incoming.Enqueue(new Packet(connectionID, messageData, (byte)Channel.Reliable));
            }

        }

        [MonoPInvokeCallback(typeof(Action<int, IntPtr, int>))]
        private static void UnreliableMessageReceivedCallback_Server(int connectionID, IntPtr dataPtr, int dataSize)
        {
            //Debug.Log($"Server: Unreliable message received for connection ID {connectionID}");

            if (dataSize > Instance.mtu)
            {
                Instance.CloseConnection(connectionID);
            }
            else
            {
                byte[] messageData = CustomByteArrayPool.Retrieve(dataSize);
                Marshal.Copy(dataPtr, messageData, 0, dataSize);

                Instance._incoming.Enqueue(new Packet(connectionID, messageData, (byte)Channel.Unreliable));
            }
        }


        public int nextConnID = 0;

        public static WebGLServerSocket Instance;

        public WebGLServerSocket()
        {
            if (Instance == null)
            {
                Instance = this;
            }
        }


        private ConcurrentQueue<LocalConnectionState> _localConnectionStates = new ConcurrentQueue<LocalConnectionState>();
        private static ConcurrentQueue<RemoteConnectionEvent> _remoteConnectionEvents = new ConcurrentQueue<RemoteConnectionEvent>();

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


            WebGLWebRTC._RegisterICEServers(ICEServer.GetFullString());

            WebGLWebRTC.InitializeServerCallbacks(
                RemoteChannelClosedCallback_Server,
                RemoteChannelOpenedCallback_Server,
                ReliableMessageReceivedCallback_Server,
                UnreliableMessageReceivedCallback_Server,
                OnOfferCreated,
                CanoeWebRTC.candidateCollectDuration,
                (CanoeWebRTC._onlyAllowRelay ? 1 : 0)
            );



            ResetQueues();

            _localConnectionStates.Enqueue(LocalConnectionState.Started);


            return true;

        }

        public RemoteConnectionState GetConnectionState(int connectionId)
        {
            int isConnected = WebGLWebRTC._GetRemoteConnectionState(connectionId);
            if (isConnected == 0)
            {
                return RemoteConnectionState.Stopped;
            }
            else
            {
                return RemoteConnectionState.Started;
            }
        }


        public bool StopServer()
        {

            if (base.GetLocalConnectionState() == LocalConnectionState.Stopped || base.GetLocalConnectionState() == LocalConnectionState.Stopping)
                return false;

            WebGLWebRTC._CloseAllRemoteConnection();

            nextConnID = 0;

            base.SetConnectionState(LocalConnectionState.Stopping, true);
            base.SetConnectionState(LocalConnectionState.Stopped, true);


            offerTasks.Clear();

            return true;

        }

        public int CreateConnection()
        {
            int thisValue = nextConnID;
            WebGLWebRTC._CreateRemoteConnection(thisValue);
            nextConnID = (nextConnID + 1) & 0x7FFFFFFF; // wrap to 0 after reaching int.MaxValue
            return thisValue;
        }

        public bool CloseConnection(int connectionID)
        {
            if (base.GetLocalConnectionState() != LocalConnectionState.Started)
                return false;

            InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Close Connection on <b><i><color=#DDA0DD>{connectionID}</color></i></b>");
            WebGLWebRTC._CloseRemoteConnection(connectionID);
            UpdateRemoteConnectionState(RemoteConnectionState.Stopped, connectionID);
            return true;
        }

        public static void UpdateRemoteConnectionState(RemoteConnectionState remoteConnectionState, int connID)
        {
            _remoteConnectionEvents.Enqueue(new RemoteConnectionEvent(remoteConnectionState == RemoteConnectionState.Started ? true : false, connID));

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
                        base.t.NetworkManager.LogWarning($"Server is sending of {data.Length} length on the unreliable channel, while the MTU is only {base.mtu}. The channel has been changed to reliable for this send.");
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

            //Handle packets
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

        static ConcurrentDictionary<int, TaskCompletionSource<OfferAnswer>> offerTasks = new ConcurrentDictionary<int, TaskCompletionSource<OfferAnswer>>();

        public static void CreateOffer(int connectionID, TaskCompletionSource<OfferAnswer> offerCompletionSource)
        {
            offerTasks.TryAdd(connectionID, offerCompletionSource);
            WebGLWebRTC._CreateOffer(connectionID);

        }

        [MonoPInvokeCallback(typeof(Action<int, string>))]
        private static void OnOfferCreated(int connectionID, string jsonString)
        {
            TaskCompletionSource<OfferAnswer> taskCompletionSource;
            offerTasks.TryRemove(connectionID, out taskCompletionSource);

            OfferAnswer offerAnswer = JsonUtility.FromJson<OfferAnswer>(jsonString);

            if (offerAnswer.error)
            {
                taskCompletionSource.SetResult(offerAnswer); // could instead set exception but for now this gives control to the signal manager which is what should be handling this anyways
                Instance.CloseConnection(connectionID);
            }
            else
            {
                taskCompletionSource.SetResult(offerAnswer);
            }
            taskCompletionSource = null;

        }



        public static void HandleResponse(int connectionID, OfferAnswer answer)
        {
            InstanceFinder.NetworkManager.Log("<color=#FFA500>[Server]</color> Handling Response");
            var jsonString = JsonUtility.ToJson(answer);
            WebGLWebRTC._HandleAnswerToOffer(connectionID, jsonString);
        }

        //maybe add a callback for when handling response fails?
        //currently it closes peer connection within jslib
        //which should clear it




        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendToOne(int connID, byte channelID, byte[] data)
        {
            if (channelID == (byte)Channel.Reliable)
            {
                GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    IntPtr dataPtr = dataHandle.AddrOfPinnedObject();
                    WebGLWebRTC.SendReliableToClient(connID, dataPtr, data.Length);
                }
                finally
                {
                    dataHandle.Free();
                }
            }
            else
            {
                GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    IntPtr dataPtr = dataHandle.AddrOfPinnedObject();
                    WebGLWebRTC.SendUnreliableToClient(connID, dataPtr, data.Length);
                }
                finally
                {
                    dataHandle.Free();
                }

            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendToAll(byte channelID, byte[] data)
        {
            if (channelID == (byte)Channel.Reliable)
            {

                GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    IntPtr dataPtr = dataHandle.AddrOfPinnedObject();
                    WebGLWebRTC.SendReliableToAllClients(dataPtr, data.Length);
                }
                finally
                {
                    dataHandle.Free();
                }

            }
            else
            {
                GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    IntPtr dataPtr = dataHandle.AddrOfPinnedObject();
                    WebGLWebRTC.SendUnreliableToAllClients(dataPtr, data.Length);
                }
                finally
                {
                    dataHandle.Free();
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