#if !UNITY_WEBGL || UNITY_EDITOR
using FishNet.Managing;
using FishNet.Transporting.CanoeWebRTC.Client;
using FishNet.Transporting.CanoeWebRTC.Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Profiling;

namespace FishNet.Transporting.CanoeWebRTC
{
    public class Connection
    {

        public int ConnectionID = -1;

        public bool closing = false;

        public RTCPeerConnection localConnection;

        RTCDataChannel reliableDataChannel;
        RTCDataChannel unreliableDataChannel;

        //purely for keeping alive...
        RTCDataChannel REMOTE_reliableDataChannel;
        RTCDataChannel REMOTE_unreliableDataChannel;


        public List<RTCIceCandidate> iceCandidates = new List<RTCIceCandidate>();

        public ClientSocket _clientSocket;
        public ServerSocket _serverSocket;


        public int currentRemoteChannels = 0;

        public string debugHeader = "";
        public Connection(ClientSocket clientSocket, ServerSocket serverSocket, int connectionID = -1)
        {
            if (connectionID == -1)
            {
                //Debug.Log("New Local Client Created");
                debugHeader = "<color=#77DD77>[Client]</color>";
            }
            else
            {
                //Debug.Log("New Remote Client Created");
                debugHeader = "<color=#FFA500>[Server]</color>";

            }

            _clientSocket = clientSocket;
            _serverSocket = serverSocket;
            ConnectionID = connectionID;

            var iceServers = new List<RTCIceServer>();
            foreach (ICEServer s in CanoeWebRTC._iceServers)
            {
                if (s.username == null || s.username == "")
                    iceServers.Add(new RTCIceServer
                    {
                        urls = new[] { s.url }
                    });
                else
                    iceServers.Add(new RTCIceServer
                    {
                        urls = new[] { s.url },
                        username = s.username,
                        credential = s.credential,
                        credentialType = RTCIceCredentialType.Password
                    });
            }

            var configuration = new RTCConfiguration
            {
                iceTransportPolicy = (CanoeWebRTC._onlyAllowRelay ? RTCIceTransportPolicy.Relay : RTCIceTransportPolicy.All),
                iceServers = iceServers.ToArray()
            };

            localConnection = new RTCPeerConnection();

            localConnection.SetConfiguration(ref configuration);

            // Set up the data channel for the local connection
            unreliableDataChannel = localConnection.CreateDataChannel("Unreliable", new RTCDataChannelInit()
            {
                ordered = false,
                maxRetransmits = 0
            });

            reliableDataChannel = localConnection.CreateDataChannel("Reliable", new RTCDataChannelInit()
            {
                ordered = true
            });

            //reliableDataChannel.OnOpen = () => Debug.Log($"reliable channel opened on {ID}");
            //reliableDataChannel.OnClose = () => Debug.Log($"reliable channel CLOSED on {ID}");
            //unreliableDataChannel.OnOpen = () => Debug.Log($"unreliable channel opened on {ID}");
            //unreliableDataChannel.OnClose = () => Debug.Log($"unreliable channel CLOSED on {ID}");


            reliableDataChannel.OnError = (e) =>
            {
                InstanceFinder.NetworkManager.LogError($"{debugHeader} ERROR - Local Reliable Channel\nType:{e.errorType} Message:{e.message}");
                if (!closing)
                    RemoteChannelsClosed();
            };
            unreliableDataChannel.OnError = (e) =>
            {
                InstanceFinder.NetworkManager.LogError($"{debugHeader} ERROR - Local Unreliable Channel\nType:{e.errorType} Message:{e.message}");
                if (!closing)
                    RemoteChannelsClosed();
            };


            // Set up the remote connection to handle incoming data channels
            localConnection.OnDataChannel = (RTCDataChannel channel) =>
            {

                if (channel.Label == "Unreliable")
                {
                    InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Unreliable Channel received");
                    REMOTE_unreliableDataChannel = channel;
                    channel.OnMessage = ReceiveUnreliableMessage;
                    channel.OnError = (e) =>
                    {
                        InstanceFinder.NetworkManager.LogError($"{debugHeader} Error... Remote Unreliable Channel\nType:{e.errorType} Message:{e.message}");
                        if (!closing)
                            RemoteChannelsClosed();
                    };

                    channel.OnOpen = () =>
                    {
                        InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Unreliable Channel Opened");
                    };
                    channel.OnClose = () =>
                    {
                        InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Unreliable Channel Closed");
                        if (!closing)
                            RemoteChannelsClosed();
                    };
                }
                else
                {
                    InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Reliable Channel received");
                    REMOTE_reliableDataChannel = channel;
                    channel.OnMessage = ReceiveReliableMessage;
                    channel.OnError = (e) =>
                    {
                        InstanceFinder.NetworkManager.LogError($"{debugHeader} Error... Remote Reliable Channel\nType:{e.errorType} Message:{e.message}");
                        if (!closing)
                            RemoteChannelsClosed();
                    };

                    //these never go off for some reason.. but I guess it makes sense? its opening remotely so yea
                    channel.OnOpen = () =>
                    {
                        InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Reliable Channel Opened");
                    };
                    channel.OnClose = () =>
                    {
                        InstanceFinder.NetworkManager.Log($"{debugHeader} Remote Reliable Channel Closed");
                        if (!closing)
                            RemoteChannelsClosed();
                    };
                }

                currentRemoteChannels++;
                if (currentRemoteChannels == 2)
                {
                    RemoteChannelsOpened();
                }

            };

            // handle new ICE candidates generated locally
            localConnection.OnIceCandidate = (RTCIceCandidate candidate) =>
            {
                if (candidate != null)
                {
                    if (candidate.Protocol == RTCIceProtocol.Udp)
                    {
                        InstanceFinder.NetworkManager.Log($"{debugHeader} New local ICE candidate: {candidate.Candidate}");

                        iceCandidates.Add(candidate);
                    }
                }
            };

            localConnection.OnIceConnectionChange = (RTCIceConnectionState state) =>
            {
                InstanceFinder.NetworkManager.Log($"{debugHeader} ICE Connection State changed to: <b><i><color=#DDA0DD>{state}</color></i></b>");
            };

            localConnection.OnConnectionStateChange = (RTCPeerConnectionState state) =>
            {
                InstanceFinder.NetworkManager.Log($"{debugHeader} Peer Connection State changed to: <b><i><color=#DDA0DD>{state}</color></i></b>");

            };


        }


        public void RemoteChannelsOpened()
        {

            if (ConnectionID != -1)
            {
                _serverSocket.UpdateRemoteConnectionState(RemoteConnectionState.Started, ConnectionID);
            }
            else
            {
                _clientSocket.UpdateLocalConnectionState(LocalConnectionState.Started);

            }

            //setup sending threads!

            Thread u = new Thread(() =>
            {
                sendThreadConfig sendThreadConfig = new sendThreadConfig(this, Channel.Unreliable);

                SendThread(sendThreadConfig);

            });
            unreliableSendThread = u;
            unreliableSendThread.IsBackground = true;
            unreliableSendThread.Name = $"UnreliableSendLoop {ConnectionID}";
            unreliableSendThread.Start();

            Thread r = new Thread(() =>
            {
                sendThreadConfig sendThreadConfig = new sendThreadConfig(this, Channel.Reliable);

                SendThread(sendThreadConfig);
            });
            reliableSendThread = r;
            reliableSendThread.IsBackground = true;
            reliableSendThread.Name = $"ReliableSendLoop {ConnectionID}";
            reliableSendThread.Start();

        }

        public void RemoteChannelsClosed()
        {
            closing = true;

            if (ConnectionID != -1)
            {
                //_serverSocket.UpdateRemoteConnectionState(somenewState, ConnectionID);
                _serverSocket.CloseConnection(ConnectionID);
            }
            else
            {
                _clientSocket.UpdateLocalConnectionState(LocalConnectionState.Stopped);
            }

        }

        //maybe split this so we are not doing an if check each time
        public void ReceiveUnreliableMessage(byte[] bytes)
        {
            //messages received here are from the other client
            //Debug.Log("Receiving a ureliable message");
            if (ConnectionID == -1)
            {
                _clientSocket._incoming.Enqueue(new Packet(0, bytes, (byte)Channel.Unreliable));

            }
            else
            {
                if (bytes.Length > _serverSocket.mtu)
                {
                    _serverSocket.CloseConnection(ConnectionID);
                }
                else
                {
                    _serverSocket._incoming.Enqueue(new Packet(ConnectionID, bytes, (byte)Channel.Unreliable));
                }
            }

        }

        public void ReceiveReliableMessage(byte[] bytes)
        {
            //messages received here are from the other client


            if (ConnectionID == -1)
            {
                _clientSocket._incoming.Enqueue(new Packet(0, bytes, (byte)Channel.Reliable));

            }
            else
            {

                if (bytes.Length > _serverSocket.mtu)
                {
                    _serverSocket.CloseConnection(ConnectionID);
                }
                else
                {
                    _serverSocket._incoming.Enqueue(new Packet(ConnectionID, bytes, (byte)Channel.Reliable));
                }
            }

        }

        //sending stuff
        public Thread unreliableSendThread;
        public Thread reliableSendThread;

        public ConcurrentQueue<byte[]> unreliableSends = new ConcurrentQueue<byte[]>();
        public ManualResetEventSlim unreliablePending = new ManualResetEventSlim(false);
        public ConcurrentQueue<byte[]> reliableSends = new ConcurrentQueue<byte[]>();
        public ManualResetEventSlim reliablePending = new ManualResetEventSlim(false);


        public struct sendThreadConfig
        {
            public readonly Channel channel;
            public readonly ConcurrentQueue<byte[]> sendQueue;
            public readonly ManualResetEventSlim sendPending;
            public readonly RTCDataChannel dataChannel;
            public readonly CommonSocket commonSocket;

            public sendThreadConfig(Connection connection, Channel channel)
            {
                this.channel = channel;
                sendQueue = channel == Channel.Unreliable ? connection.unreliableSends : connection.reliableSends;
                sendPending = channel == Channel.Unreliable ? connection.unreliablePending : connection.reliablePending;
                dataChannel = channel == Channel.Unreliable ? connection.unreliableDataChannel : connection.reliableDataChannel;
                commonSocket = connection.ConnectionID == -1 ? connection._clientSocket : connection._serverSocket;
            }

            public void Deconstruct(out Channel channel, out ConcurrentQueue<byte[]> sendQueue, out ManualResetEventSlim sendPending, out RTCDataChannel dataChannel, out CommonSocket commonSocket)
            {
                channel = this.channel;
                sendQueue = this.sendQueue;
                sendPending = this.sendPending;
                dataChannel = this.dataChannel;
                commonSocket = this.commonSocket;
            }
        }


        void SendThread(sendThreadConfig config)
        {

            Profiler.BeginThreadProfiling("WebRTCConnection", $"SendLoop {ConnectionID}");

            bool dispose = false;

            var (channel, sendQueue, sendPending, dataChannel, socket) = config;

            try
            {

                while (localConnection.ConnectionState == RTCPeerConnectionState.Connected)
                {
                    // wait for message
                    sendPending.Wait();
                    sendPending.Reset();

                    if (dataChannel.ReadyState == RTCDataChannelState.Open)
                    {
                        while (sendQueue.TryDequeue(out byte[] data))
                        {
                            dataChannel.Send(data);
                        }
                    }

                }

                if (dispose)
                {
                    Profiler.EndThreadProfiling();
                    InstanceFinder.NetworkManager.LogError("ERROR Closing connection due to exception");

                    if (!closing)
                        RemoteChannelsClosed();
                }


            }
            catch (ThreadInterruptedException)
            {
                dispose = true;
                //Debug.Log(e);
            }
            catch (ThreadAbortException)
            {
                dispose = true;
                //Debug.Log(e);
            }
            catch (Exception)
            {
                dispose = true;
                //Debug.Log(e);
            }
        }









        readonly object disposedLock = new object();
        bool hasDisposed = false;

        public void CloseAll()
        {

            if (hasDisposed) { return; }

            lock (disposedLock)
            {

                if (hasDisposed) { return; }
                hasDisposed = true;

                unreliableSendThread?.Interrupt();
                reliableSendThread?.Interrupt();

                try
                {
                    localConnection?.Dispose();
                    localConnection = null;
                    reliableDataChannel?.Dispose();
                    reliableDataChannel = null;
                    unreliableDataChannel?.Dispose();
                    unreliableDataChannel = null;
                    REMOTE_reliableDataChannel?.Dispose();
                    REMOTE_reliableDataChannel = null;
                    REMOTE_unreliableDataChannel?.Dispose();
                    REMOTE_unreliableDataChannel = null;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                unreliablePending.Dispose();
                reliablePending.Dispose();

                while (unreliableSends.TryDequeue(out byte[] someData))
                {

                }
                while (reliableSends.TryDequeue(out byte[] someData))
                {

                }

            }
        }

    }
}
#endif