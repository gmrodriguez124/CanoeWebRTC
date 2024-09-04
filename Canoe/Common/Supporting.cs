using System;
using System.Collections.Generic;
using System.Text;

namespace FishNet.Transporting.CanoeWebRTC
{
    //same thing as fishnet but without creating a double minimum length
    public static class CustomByteArrayPool
    {

        private static Queue<byte[]> _byteArrays = new Queue<byte[]>();


        public static byte[] Retrieve(int minimumLength)
        {
            byte[] result = null;

            if (_byteArrays.Count > 0)
                result = _byteArrays.Dequeue();

            int doubleMinimumLength = (minimumLength);
            if (result == null)
                result = new byte[doubleMinimumLength];
            else if (result.Length < minimumLength)
                Array.Resize(ref result, doubleMinimumLength);


            return result;
        }


        public static void Store(byte[] buffer)
        {

            if (_byteArrays.Count > 300)
                return;
            _byteArrays.Enqueue(buffer);
        }
    }

    [System.Serializable]
    public class ICEServer
    {
        public string url;
        public string username;
        public string credential;


        public static string GetFullString()
        {
            var iceServers = CanoeWebRTC._iceServers;

            if (iceServers == null || iceServers.Count == 0)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();

            foreach (ICEServer server in iceServers)
            {
                sb.Append(server.url ?? "").Append("__")
                  .Append(server.username ?? "").Append("__")
                  .Append(server.credential ?? "").Append(";;");
            }

            if (sb.Length > 0)
            {
                sb.Length -= 2;
            }

            return sb.ToString();
        }
    }

    public struct TrickledICE
    {
        public string candidate;
        public int sender;
        public int receiver;

        public TrickledICE(string candidate, int sender, int receiver)
        {
            this.candidate = candidate;
            this.sender = sender;
            this.receiver = receiver;
        }

        public byte[] Serialize()
        {
            return Encoding.UTF8.GetBytes(candidate);
        }

        public static TrickledICE Deserialize(ArraySegment<byte> data)
        {
            string candidate = Encoding.UTF8.GetString(data.Array, data.Offset, data.Count);

            return new TrickledICE(candidate, -1, -1);
        }
    }

    public struct OfferAnswer
    {
        public string sdp;
        public string[] candidates;

        public bool error;
        public string errorMessage;

        public OfferAnswer(string sdp, string[] candidates, bool error = false, string errorMessage = null)
        {
            this.sdp = sdp;
            this.candidates = candidates;
            this.error = error;
            this.errorMessage = error ? errorMessage : null;
        }

        public byte[] Serialize()
        {
            int sdpLength = Encoding.UTF8.GetByteCount(sdp);
            int totalSize = sizeof(int) + sdpLength + sizeof(int) + sizeof(bool); 

            foreach (string candidate in candidates)
            {
                totalSize += sizeof(int) + Encoding.UTF8.GetByteCount(candidate);
            }

            if (error && errorMessage != null)
            {
                totalSize += sizeof(int) + Encoding.UTF8.GetByteCount(errorMessage); 
            }

            byte[] result = CustomByteArrayPool.Retrieve(totalSize);
            int offset = 0;

            BitConverter.GetBytes(sdpLength).CopyTo(result, offset);
            offset += sizeof(int);
            Encoding.UTF8.GetBytes(sdp, 0, sdp.Length, result, offset);
            offset += sdpLength;

            BitConverter.GetBytes(candidates.Length).CopyTo(result, offset);
            offset += sizeof(int);

            foreach (string candidate in candidates)
            {
                int candidateLength = Encoding.UTF8.GetByteCount(candidate);
                BitConverter.GetBytes(candidateLength).CopyTo(result, offset);
                offset += sizeof(int);
                Encoding.UTF8.GetBytes(candidate, 0, candidate.Length, result, offset);
                offset += candidateLength;
            }

            BitConverter.GetBytes(error).CopyTo(result, offset);
            offset += sizeof(bool);

            if (error && errorMessage != null)
            {
                int errorMessageLength = Encoding.UTF8.GetByteCount(errorMessage);
                BitConverter.GetBytes(errorMessageLength).CopyTo(result, offset);
                offset += sizeof(int);
                Encoding.UTF8.GetBytes(errorMessage, 0, errorMessage.Length, result, offset);
                offset += errorMessageLength;
            }

            return result;
        }

        public static OfferAnswer Deserialize(ArraySegment<byte> data)
        {
            int offset = 0;

            int sdpLength = BitConverter.ToInt32(data.Array, data.Offset + offset);
            offset += sizeof(int);
            string sdp = Encoding.UTF8.GetString(data.Array, data.Offset + offset, sdpLength);
            offset += sdpLength;

            int candidateCount = BitConverter.ToInt32(data.Array, data.Offset + offset);
            offset += sizeof(int);
            string[] candidates = new string[candidateCount];

            for (int i = 0; i < candidateCount; i++)
            {
                int candidateLength = BitConverter.ToInt32(data.Array, data.Offset + offset);
                offset += sizeof(int);
                candidates[i] = Encoding.UTF8.GetString(data.Array, data.Offset + offset, candidateLength);
                offset += candidateLength;
            }

            bool error = BitConverter.ToBoolean(data.Array, data.Offset + offset);
            offset += sizeof(bool);

            string errorMessage = null;
            if (error)
            {
                int errorMessageLength = BitConverter.ToInt32(data.Array, data.Offset + offset);
                offset += sizeof(int);
                errorMessage = Encoding.UTF8.GetString(data.Array, data.Offset + offset, errorMessageLength);
                offset += errorMessageLength;
            }

            return new OfferAnswer(sdp, candidates, error, errorMessage);
        }
    }


    public struct Packet
    {
        public readonly int ConnectionID;
        public readonly byte[] Data;
        public readonly byte Channel;

        public Packet(int connectionID, byte[] segment, byte channel)
        {
            ConnectionID = connectionID;
            Data = segment;
            Channel = channel;
        }

        public Packet(int connectionID, ArraySegment<byte> segment, byte channel, int mtu)
        {

            int arraySize = Math.Max(segment.Count + sizeof(ushort), mtu);
            Data = CustomByteArrayPool.Retrieve(arraySize);

            ushort originalDataLength = (ushort)segment.Count;
            Data[0] = (byte)(originalDataLength & 0xFF);     
            Data[1] = (byte)((originalDataLength >> 8) & 0xFF); 



            Buffer.BlockCopy(segment.Array, segment.Offset, Data, sizeof(ushort), segment.Count);
            ConnectionID = connectionID;
            Channel = channel;
        }


        public ArraySegment<byte> GetArraySegment()
        {
            ushort originalDataLength = BitConverter.ToUInt16(Data, 0);

            return new ArraySegment<byte>(Data, sizeof(ushort), originalDataLength);
        }


        public void Dispose()
        {
            CustomByteArrayPool.Store(Data);
        }

    }


    public struct RemoteConnectionEvent
    {
        public readonly bool Connected;
        public readonly int ConnectionId;
        public RemoteConnectionEvent(bool connected, int connectionId)
        {
            Connected = connected;
            ConnectionId = connectionId;
        }
    }

}