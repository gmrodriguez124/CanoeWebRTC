using System;
using System.Collections.Concurrent;
using System.Collections.Generic;



namespace FishNet.Transporting.CanoeWebRTC
{
    public class CommonSocket
    {
        public Transport t;

        public int mtu;


        internal Queue<Packet> _outgoing = new Queue<Packet>();
        public void Send(int connID, byte channelID, ArraySegment<byte> segment)
        {
            Packet outgoing = new Packet(connID, segment, channelID, mtu);
            _outgoing.Enqueue(outgoing);
        }




        internal ConcurrentQueue<Packet> _incoming = new ConcurrentQueue<Packet>();




        private LocalConnectionState _connectionState = LocalConnectionState.Stopped;

        public LocalConnectionState GetLocalConnectionState()
        {
            return _connectionState;
        }

        public void SetConnectionState(LocalConnectionState connectionState, bool asServer)
        {
            //If state hasn't changed.
            if (connectionState == _connectionState)
                return;

            _connectionState = connectionState;
            if (asServer)
                t.HandleServerConnectionState(new ServerConnectionStateArgs(connectionState, t.Index));
            else
                t.HandleClientConnectionState(new ClientConnectionStateArgs(connectionState, t.Index));
        }








        internal void ClearGenericQueue<T>(ref ConcurrentQueue<T> queue)
        {
            while (queue.TryDequeue(out _)) { }
        }

        internal void ClearPacketQueue(ref ConcurrentQueue<Packet> queue)
        {
            while (queue.TryDequeue(out Packet p))
                p.Dispose();
        }

        internal void ClearPacketQueue(ref Queue<Packet> queue)
        {
            int count = queue.Count;
            for (int i = 0; i < count; i++)
            {
                Packet p = queue.Dequeue();
                p.Dispose();
            }
        }


    }
}