#if !UNITY_WEBGL || UNITY_EDITOR
using FishNet.Managing;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;


namespace FishNet.Transporting.CanoeWebRTC.Client
{
    public class ClientSocket : CommonSocket
    {

        public Connection connectionWithServer;

        private ConcurrentQueue<LocalConnectionState> _localConnectionStates = new ConcurrentQueue<LocalConnectionState>();

        public bool StartClient(Transport t, int mtu)
        {

            if (base.GetLocalConnectionState() != LocalConnectionState.Stopped)
                return false;

            base.t = t;
            base.mtu = mtu;

            _localConnectionStates.Enqueue(LocalConnectionState.Starting);

            connectionWithServer = new Connection(this, null, -1);


            ResetQueues();

            UpdateLocalConnectionState(LocalConnectionState.Starting);

            return true;

        }

        public bool StopClient()
        {
            base.SetConnectionState(LocalConnectionState.Stopping, false);

            connectionWithServer.CloseAll();
            connectionWithServer = null;

            ResetQueues();

            if (base.GetLocalConnectionState() != LocalConnectionState.Stopped)
                base.SetConnectionState(LocalConnectionState.Stopped, false);
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
                        base.t.NetworkManager.LogWarning($"<color=#77DD77>[Client]</color> is sending of {data.Length} length on the unreliable channel, while the MTU is only {base.mtu}. The channel has been changed to reliable for this send.");
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
                // If stopped try to kill task
                if (localState == LocalConnectionState.Stopped)
                {
                    //StopSocketOnThread();
                    //StopClient();
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
                //connectionWithServer.SendReliableMessage(data);
                connectionWithServer.reliableSends.Enqueue(data);
                connectionWithServer.reliablePending.Set();
            }
            else
            {
                //connectionWithServer.SendUnreliableMessage(data);
                connectionWithServer.unreliableSends.Enqueue(data);
                connectionWithServer.unreliablePending.Set();

            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetQueues()
        {

            base.ClearGenericQueue<LocalConnectionState>(ref _localConnectionStates);
            base.ClearPacketQueue(ref _incoming);
            base.ClearPacketQueue(ref _outgoing);
        }

    }
}
#endif