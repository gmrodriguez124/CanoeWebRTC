using AOT;
using FishNet.Managing;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;


namespace FishNet.Transporting.CanoeWebRTC.Client
{
    public class WebGLClientSocket : CommonSocket
    {

        [MonoPInvokeCallback(typeof(Action))]
        private static void OnRemoteChannelClosed()
        {
            InstanceFinder.NetworkManager.Log("<color=#77DD77>[Client]</color> Remote channel closed.");

            Instance.UpdateLocalConnectionState(LocalConnectionState.Stopped);

        }

        [MonoPInvokeCallback(typeof(Action))]
        private static void OnRemoteChannelOpened()
        {
            InstanceFinder.NetworkManager.Log("<color=#77DD77>[Client]</color> Remote channel opened.");

            Instance.UpdateLocalConnectionState(LocalConnectionState.Started);


        }

        [MonoPInvokeCallback(typeof(Action<IntPtr, int>))]
        private static void OnReliableMessageReceived(IntPtr dataPtr, int dataSize)
        {
            byte[] messageData = CustomByteArrayPool.Retrieve(dataSize);
            Marshal.Copy(dataPtr, messageData, 0, dataSize);

            Instance._incoming.Enqueue(new Packet(0, messageData, (byte)Channel.Reliable));

        }

        [MonoPInvokeCallback(typeof(Action<IntPtr, int>))]
        private static void OnUnreliableMessageReceived(IntPtr dataPtr, int dataSize)
        {
            byte[] messageData = CustomByteArrayPool.Retrieve(dataSize);

            Marshal.Copy(dataPtr, messageData, 0, dataSize);

            Instance._incoming.Enqueue(new Packet(0, messageData, (byte)Channel.Unreliable));

        }






        public static WebGLClientSocket Instance;

        public WebGLClientSocket()
        {
            if (Instance == null)
            {
                Instance = this;
            }
        }


        private ConcurrentQueue<LocalConnectionState> _localConnectionStates = new ConcurrentQueue<LocalConnectionState>();

        public bool StartClient(Transport t, int mtu)
        {


            if (base.GetLocalConnectionState() != LocalConnectionState.Stopped)
                return false;

            base.t = t;
            base.mtu = mtu;


            WebGLWebRTC._RegisterICEServers(ICEServer.GetFullString());

            WebGLWebRTC.InitializeClientCallbacks(
                OnRemoteChannelClosed,
                OnRemoteChannelOpened,
                OnReliableMessageReceived,
                OnUnreliableMessageReceived,
                RespondToOfferCallback,
                CanoeWebRTC.candidateCollectDuration,
                (CanoeWebRTC._onlyAllowRelay ? 1 : 0)
            );

            WebGLWebRTC.CreateClient();

            ResetQueues();

            UpdateLocalConnectionState(LocalConnectionState.Starting);

            return true;

        }

        public bool StopClient()
        {
            base.SetConnectionState(LocalConnectionState.Stopping, false);

            WebGLWebRTC.CloseClient();

            ResetQueues();

            if (base.GetLocalConnectionState() != LocalConnectionState.Stopped)
                base.SetConnectionState(LocalConnectionState.Stopped, false);

            answerTask = null;

            return true;
        }


        public void UpdateLocalConnectionState(LocalConnectionState newState)
        {

            _localConnectionStates.Enqueue(newState);

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IterateOutgoing()
        {
            if (base.GetLocalConnectionState() != LocalConnectionState.Started)
            {
                //Debug.Log($"<color=#FFA500>[Client]</color> Tried to iterate outgoing while client was not opened");
                base.ClearPacketQueue(ref base._outgoing);
            }
            else
            {
                int count = base._outgoing.Count;
                for (int i = 0; i < count; i++)
                {
                    Packet outgoing = base._outgoing.Dequeue();

                    byte[] data = outgoing.Data;
                    byte channelID = outgoing.Channel;


                    //If over the MTU.
                    if (outgoing.Channel == (byte)Channel.Unreliable && data.Length > base.mtu)
                    {
                        InstanceFinder.NetworkManager.LogWarning($"<color=#77DD77>[Client]</color> Client is sending of {data.Length} length on the unreliable channel, while the MTU is only {base.mtu}. The channel has been changed to reliable for this send.");
                        channelID = (byte)Channel.Reliable;
                    }

                    SendToServer(channelID, data);

                    outgoing.Dispose();

                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void IterateIncoming()
        {

            while (_localConnectionStates.TryDequeue(out LocalConnectionState result))
            {
                InstanceFinder.NetworkManager.Log($"<color=#77DD77>[Client]</color> Local connection state set to <b><i><color=#DDA0DD>{result}</color></i></b>");

                base.SetConnectionState(result, false);
            }



            //Not yet started, cannot continue.
            LocalConnectionState localState = base.GetLocalConnectionState();
            if (localState != LocalConnectionState.Started)
            {
                ResetQueues();
                //If stopped try to kill task.
                if (localState == LocalConnectionState.Stopped)
                {
                    return;
                }
            }




            /* Incoming. */
            while (_incoming.TryDequeue(out Packet incoming))
            {
                ClientReceivedDataArgs dataArgs = new ClientReceivedDataArgs(
                    incoming.GetArraySegment(),
                    (Channel)incoming.Channel, base.t.Index);
                base.t.HandleClientReceivedDataArgs(dataArgs);
                //Dispose of packet.
                incoming.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendToServer(byte channelID, byte[] data)
        {

            if (channelID == (byte)Channel.Reliable)
            {

                SendReliableData(data);
            }
            else
            {

                SendUnreliableData(data);

            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetQueues()
        {

            base.ClearGenericQueue<LocalConnectionState>(ref _localConnectionStates);
            base.ClearPacketQueue(ref _incoming);
            base.ClearPacketQueue(ref _outgoing);
        }






        static TaskCompletionSource<OfferAnswer> answerTask;

        public static void ReceivedOffer(OfferAnswer offer, TaskCompletionSource<OfferAnswer> offerCompletionSource)
        {
            answerTask = offerCompletionSource;

            var jsonString = JsonUtility.ToJson(offer);
            WebGLWebRTC._HandleOffer(jsonString);
        }


        [MonoPInvokeCallback(typeof(Action<string>))]
        private static void RespondToOfferCallback(string answer)
        {
            InstanceFinder.NetworkManager.Log($"<color=#77DD77>[Client]</color> Responding to offer from host");

            OfferAnswer offerAnswer = JsonUtility.FromJson<OfferAnswer>(answer);


            if (offerAnswer.error)
            {
                answerTask.SetResult(offerAnswer); // could instead set exception but for now this gives control to the signal manager which is what should be handling this anyways
                Instance.StopClient();
            }
            else
            {
                answerTask.SetResult(offerAnswer);
            }
            answerTask = null;

        }





        public void SendUnreliableData(byte[] data)
        {
            GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                IntPtr dataPtr = dataHandle.AddrOfPinnedObject();
                WebGLWebRTC.SendUnreliableToServer(dataPtr, data.Length);
            }
            finally
            {
                dataHandle.Free();
            }
        }

        public void SendReliableData(byte[] data)
        {
            GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                IntPtr dataPtr = dataHandle.AddrOfPinnedObject();
                WebGLWebRTC.SendReliableToServer(dataPtr, data.Length);
            }
            finally
            {
                dataHandle.Free();
            }
        }
    }
}