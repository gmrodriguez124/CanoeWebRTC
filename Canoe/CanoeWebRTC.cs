using FishNet.Managing;
using FishNet.Transporting.CanoeWebRTC.Client;
using FishNet.Transporting.CanoeWebRTC.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#if !UNITY_WEBGL || UNITY_EDITOR
using Unity.WebRTC;
#endif
using UnityEngine;



namespace FishNet.Transporting.CanoeWebRTC
{

    [DisallowMultipleComponent]
    [AddComponentMenu("FishNet/Transport/CanoeWebRTC")]
    public class CanoeWebRTC : Transport
    {

#if !UNITY_WEBGL || UNITY_EDITOR
        public static ClientSocket _client = new ClientSocket();
        public static ServerSocket _server = new ServerSocket();

#else
    public static WebGLClientSocket _client = new WebGLClientSocket();
    public static WebGLServerSocket _server = new WebGLServerSocket();
#endif





        public override string GetConnectionAddress(int connectionId)
        {
            InstanceFinder.NetworkManager.LogWarning("Connection Address is not implemented.");
            return string.Empty;
        }



        public override int GetMTU(byte channel)
        {
            _MTU = MTU;
            return _MTU;
        }



        public int MTU = 1023;
        public static int _MTU;


        public bool disableLocalConnections = false;
        public static bool _disableLocalConnections;

        public bool onlyAllowRelay = false;
        public static bool _onlyAllowRelay;

        public List<ICEServer> iceServers = new List<ICEServer>();
        public static List<ICEServer> _iceServers;



        public override bool StartConnection(bool server)
        {
            _iceServers = iceServers;
            _disableLocalConnections = disableLocalConnections;
            _onlyAllowRelay = onlyAllowRelay;

            if (server)
            {
                InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Starting");
                return _server.StartServer(this, GetMTU(0));
            }
            else
            {
                InstanceFinder.NetworkManager.Log($"<color=#77DD77>[Client]</color> Starting");
                return _client.StartClient(this, GetMTU(0));
            }
        }


        public override bool StopConnection(bool server)
        {

            if (server)
            {
                InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Stopping");
                return _server.StopServer();
            }
            else
            {
                InstanceFinder.NetworkManager.Log($"<color=#77DD77>[Client]</color> Stopping");
                return _client.StopClient();
            }
        }

        public override bool StopConnection(int connectionId, bool immediately)
        {
            InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Stopping client <b><i><color=#DDA0DD>{connectionId}</color></i></b>");
            return _server.CloseConnection(connectionId);




        }


        public override LocalConnectionState GetConnectionState(bool server)
        {
            if (server)
            {
                return _server.GetLocalConnectionState();
            }
            else
            {
                return _client.GetLocalConnectionState();
            }
        }

        public override RemoteConnectionState GetConnectionState(int connectionId)
        {
            return _server.GetConnectionState(connectionId);
        }


        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;
        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
        {
            OnClientReceivedData?.Invoke(receivedDataArgs);
        }

        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {

            _client.Send(0, channelId, segment);
        }


        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;
        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
        {
            OnServerReceivedData?.Invoke(receivedDataArgs);
        }

        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            _server.Send(connectionId, channelId, segment);
        }




        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs)
        {
            if (connectionStateArgs.ConnectionState == LocalConnectionState.Started)
            {
                StopSignalTimeout(-1);
            }

            OnClientConnectionState?.Invoke(connectionStateArgs);
        }

        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs)
        {
            OnServerConnectionState?.Invoke(connectionStateArgs);

        }

        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;
        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs)
        {

            /*
            if (connectionStateArgs.ConnectionState == RemoteConnectionState.Started)
            {
                StopSignalTimeout(connectionStateArgs.ConnectionId);
            }
            */

            OnRemoteConnectionState?.Invoke(connectionStateArgs);

        }


        public override void IterateIncoming(bool server)
        {
            if (server)
            {
                _server.IterateIncoming();
            }
            else
            {
                _client.IterateIncoming();
            }
        }

        public override void IterateOutgoing(bool server)
        {

            if (server)
            {
                _server.IterateOutgoing();
            }
            else
            {
                _client.IterateOutgoing();
            }
        }


        public override void Shutdown()
        {
            InstanceFinder.NetworkManager.Log("Shutting Down");
            if (_server.GetLocalConnectionState() != LocalConnectionState.Stopped)
                StopConnection(true);
            if (_client.GetLocalConnectionState() != LocalConnectionState.Stopped)
                StopConnection(false);
        }


        public static int CreateNewRemoteConnection()
        {
            InstanceFinder.NetworkManager.Log($"<color=#FFA500>[Server]</color> Creating new remote connection");
            return _server.CreateConnection();
        }



        //Handle Signalling

        public static void SignalShutdown()
        {
            if (_server.GetLocalConnectionState() != LocalConnectionState.Stopped)
                _server.StopServer();
            if (_client.GetLocalConnectionState() != LocalConnectionState.Stopped)
                _client.StopClient();
        }


        private static readonly int timeoutDurationMilliseconds = 2500; //this is the per operation timeout duration
        internal static readonly int candidateCollectDuration = 2000; //this is the candidate collection duration

        //Handling timeout for signalling
        public static int signalTimeoutDurationMilliseconds = 8000; //this is the overall process timeout duration
        public static CancellationTokenSource clientCancellationTokenSource;
        public static Dictionary<int, CancellationTokenSource> remoteCancellationTokenSources = new Dictionary<int, CancellationTokenSource>();

        private static CancellationToken StartSignalTimeout(int connectionID)
        {
            InstanceFinder.NetworkManager.Log("Starting signal timeout");
            CancellationToken token;

            if (connectionID != -1)
            {
                CancellationTokenSource remoteClientCancellationTokenSources = new CancellationTokenSource();
                remoteCancellationTokenSources.Add(connectionID, remoteClientCancellationTokenSources);
                token = remoteClientCancellationTokenSources.Token;

                _ = SignalTimeoutAsync(connectionID, token);
            }
            else
            {
                clientCancellationTokenSource = new CancellationTokenSource();
                token = clientCancellationTokenSource.Token;
                _ = SignalTimeoutAsync(connectionID, token);

            }

            return token;
        }

        //this occurs if it fully connects / finishes -- we consider fully connected from the clients perspective after we have connected but consider fully connected on server after we receive the answer
        private static void StopSignalTimeout(int connectionID)
        {
            if (connectionID != -1)
            {
                remoteCancellationTokenSources[connectionID].Cancel();
                remoteCancellationTokenSources[connectionID].Dispose();
                remoteCancellationTokenSources.Remove(connectionID);
            }
            else
            {
                clientCancellationTokenSource.Cancel();
                clientCancellationTokenSource.Dispose();
            }
        }

        private static async Task SignalTimeoutAsync(int connectionID, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(signalTimeoutDurationMilliseconds, cancellationToken);
                InstanceFinder.NetworkManager.LogWarning("Signal timeout occured! - consider extending the timeout duration");

                if (connectionID != -1)
                {
                    remoteCancellationTokenSources[connectionID].Cancel();

                    await Task.Delay(timeoutDurationMilliseconds + 100); //allow the operation to have time or to timeout on its own

                    if (remoteCancellationTokenSources.ContainsKey(connectionID))
                    {
                        HandleSignalTimeout(connectionID);
                    }

                }
                else
                {
                    clientCancellationTokenSource.Cancel();

                    await Task.Delay(timeoutDurationMilliseconds + 100);

                    if (clientCancellationTokenSource != null)
                    {
                        HandleSignalTimeout(-1);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                //timeout cancelled -- connection successful
                InstanceFinder.NetworkManager.Log("Signal timeout was canceled since connection was successful!");
            }
        }

        //this occurs if it times out - needs to be handled by the server on offer creation up until handle recevied answer at which point we consider a success in the signalling (before the handling but after receiving)
        private static void HandleSignalTimeout(int connectionID)
        {
            if (connectionID != -1)
            {
                remoteCancellationTokenSources[connectionID].Dispose();
                remoteCancellationTokenSources.Remove(connectionID);

                _server.CloseConnection(connectionID);
            }
            else
            {
                clientCancellationTokenSource.Dispose();
                clientCancellationTokenSource = null;
                _client.StopClient();
            }
        }

        //Server

        //Create Offer For a client from the server (us)
        public static async Task<OfferAnswer> CreateOfferForClient(int connectionID = -1)
        {

            if (_server.GetLocalConnectionState() != LocalConnectionState.Started)
            {
                return new OfferAnswer("", null, true, "Server not started");
            }



            if (connectionID == -1)
            {
                connectionID = CanoeWebRTC.CreateNewRemoteConnection();
            }

            CancellationToken token = StartSignalTimeout(connectionID);

            var timeoutTask = Task.Delay(timeoutDurationMilliseconds);

            Task<OfferAnswer> operationTask;

#if !UNITY_WEBGL || UNITY_EDITOR
            Connection connection = _server.connections[connectionID];
            operationTask = CreateOfferAsync(connection, token);
#else
    TaskCompletionSource<OfferAnswer> offerCompletionSource = new TaskCompletionSource<OfferAnswer>();
    WebGLServerSocket.CreateOffer(connectionID, offerCompletionSource);
    operationTask = offerCompletionSource.Task;
#endif

            var completedTask = await Task.WhenAny(operationTask, timeoutTask);


            if (completedTask == operationTask)
            {
                if (token.IsCancellationRequested)
                {
                    HandleSignalTimeout(connectionID);
                    return new OfferAnswer("", null, true, "Operation was cancelled before starting");

                }
                return await operationTask;
            }
            else
            {
                _server.CloseConnection(connectionID);
                InstanceFinder.NetworkManager.LogError("Timeout occurred while creating offer.");
                return new OfferAnswer("", null, true, "Timeout occurred while creating offer");
            }
        }


#if !UNITY_WEBGL || UNITY_EDITOR
        private static async Task<OfferAnswer> CreateOfferAsync(Connection clientConnection, CancellationToken token)
        {

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.Log("Operation was cancelled before starting.");
                return new OfferAnswer("", null, true, "Operation was cancelled");
            }


            RTCSessionDescriptionAsyncOperation offerOp = clientConnection.localConnection.CreateOffer();

            await WaitUntilOperationIsDone(offerOp);

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.Log("Operation was cancelled after creating offer.");
                return new OfferAnswer("", null, true, "Operation was cancelled");
            }

            if (offerOp.IsError)
            {
                InstanceFinder.NetworkManager.LogError("Failed to create offer");
                return new OfferAnswer("", null, true, "Failed to create offer");

            }

            RTCSessionDescription desc = offerOp.Desc;


            var localDescOp = clientConnection.localConnection.SetLocalDescription(ref desc);

            await WaitUntilOperationIsDone(localDescOp);

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.Log("Operation was cancelled after setting local description.");
                return new OfferAnswer("", null, true, "Operation was cancelled");
            }

            if (localDescOp.IsError)
            {
                InstanceFinder.NetworkManager.LogError("Failed to set local description");
                return new OfferAnswer("", null, true, "Failed to set local description");

            }

            string sdp = offerOp.Desc.sdp;

            try
            {
                await Task.Delay(candidateCollectDuration, token);
            }
            catch (TaskCanceledException)
            {
                InstanceFinder.NetworkManager.Log("Delay was cancelled.");
                return new OfferAnswer("", null, true, "Operation was cancelled");
            }

            OfferAnswer requestObject = new OfferAnswer(sdp, clientConnection.iceCandidates.Select(s => s.Candidate).ToArray());

            return requestObject;

        }

        private static async Task WaitUntilOperationIsDone(RTCSessionDescriptionAsyncOperation operation)
        {
            while (!operation.IsDone)
            {
                await Task.Yield();
            }
        }

        private static async Task WaitUntilOperationIsDone(RTCSetSessionDescriptionAsyncOperation operation)
        {
            while (!operation.IsDone)
            {
                await Task.Yield();
            }
        }
#endif

        //Handle Response - Server
        // this has 0 callbacks both on webgl and on standalones maybe add
        public static async Task HandleAnswerFromClient(int connectionID, OfferAnswer answer)
        {

            if (_server.GetLocalConnectionState() != LocalConnectionState.Started)
            {
                return;
            }

            if (remoteCancellationTokenSources.ContainsKey(connectionID))
            {
                if (remoteCancellationTokenSources[connectionID].IsCancellationRequested)
                {
                    StopSignalTimeout(connectionID); //make sure it gets removed - maybe ignore and allow to go through even past timeout since this only gets called if its been completed already
                    return;
                }
            }

            StopSignalTimeout(connectionID);

            var timeoutTask = Task.Delay(timeoutDurationMilliseconds);


            Task operationTask;

#if !UNITY_WEBGL || UNITY_EDITOR
            Connection connection = _server.connections[connectionID];
            operationTask = ServerReceiveAnswerAsync(connection, answer);

            var completedTask = await Task.WhenAny(operationTask, timeoutTask);
            if (completedTask == operationTask)
            {

                await operationTask;

            }
            else
            {
                _server.CloseConnection(connectionID);
                InstanceFinder.NetworkManager.Log("Timeout occurred while handling response from client.");

            }

#else

    WebGLServerSocket.HandleResponse(connectionID, answer);
    return; // No async operation to await in WebGL case

#endif

        }


#if !UNITY_WEBGL || UNITY_EDITOR
        private static async Task ServerReceiveAnswerAsync(Connection remoteConnection, OfferAnswer answer)
        {

            RTCSessionDescription desc = new RTCSessionDescription
            {
                type = RTCSdpType.Answer,
                sdp = answer.sdp
            };

            var remoteDescOp = remoteConnection.localConnection.SetRemoteDescription(ref desc);

            await WaitUntilOperationIsDone(remoteDescOp);

            if (remoteDescOp.IsError)
            {
                InstanceFinder.NetworkManager.LogError("Failed to set remote description");

            }

            //Debug.Log("THE FOLLOWING IS WHAT THE Client SENT TO US");
            //Debug.Log("-------------------------------------------");

            foreach (var iceCandidate in answer.candidates)
            {
                //Debug.Log(iceCandidate);
                var candidateInit = new RTCIceCandidateInit
                {
                    candidate = iceCandidate,
                    sdpMid = "0",
                    sdpMLineIndex = 0
                };
                remoteConnection.localConnection.AddIceCandidate(new RTCIceCandidate(candidateInit));
            }
            //Debug.Log("-------------------------------------------");

        }


#endif

        //CLIENT
        //Create answer for server / Recieve offer from server
        public static async Task<OfferAnswer> CreateAnswerForServer(OfferAnswer offer)
        {

            if (_client.GetLocalConnectionState() != LocalConnectionState.Starting)
            {
                return new OfferAnswer("", null, true, "Client not started");
            }

            CancellationToken token = StartSignalTimeout(-1);


            var timeoutTask = Task.Delay(timeoutDurationMilliseconds);

            Task<OfferAnswer> operationTask;

#if !UNITY_WEBGL || UNITY_EDITOR
            Connection connection = _client.connectionWithServer;
            operationTask = CreateResponseAsync(connection, offer, token);
#else
        //Debug.Log("Received Offer from server1111");
        TaskCompletionSource<OfferAnswer> offerCompletionSource = new TaskCompletionSource<OfferAnswer>();
        WebGLClientSocket.ReceivedOffer(offer, offerCompletionSource);
        operationTask = offerCompletionSource.Task;
#endif

            // Wait for either the operation task or the timeout task to complete
            var completedTask = await Task.WhenAny(operationTask, timeoutTask);



            if (completedTask == operationTask)
            {
                if (token.IsCancellationRequested)
                {
                    HandleSignalTimeout(-1);
                    return new OfferAnswer("", null, true, "Signal timeout error");

                }

                // The main task completed within the timeout
                return await operationTask;
            }
            else
            {
                // Timeout occurred
                _client.StopClient();
                InstanceFinder.NetworkManager.LogError("Timeout occurred while creating answer for server.");
                return new OfferAnswer("", null, true, "Timeout occurred while creating offer");

            }
        }


#if !UNITY_WEBGL || UNITY_EDITOR
        private static async Task<OfferAnswer> CreateResponseAsync(Connection connection, OfferAnswer offer, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.LogWarning("Operation was cancelled before starting.");
                return new OfferAnswer("", null, true, "Operation was cancelled before starting");
            }

            // Debug.Log("Received an Offer");

            RTCSessionDescription descOffer = new RTCSessionDescription
            {
                type = RTCSdpType.Offer,
                sdp = offer.sdp
            };

            var remoteDescOp = connection.localConnection.SetRemoteDescription(ref descOffer);

            await WaitUntilOperationIsDone(remoteDescOp);

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.LogWarning("Operation was cancelled after setting remote description.");
                return new OfferAnswer("", null, true, "Operation was cancelled after setting remote description");
            }

            if (remoteDescOp.IsError)
            {
                InstanceFinder.NetworkManager.LogWarning("Failed to set remote description");
                return new OfferAnswer("", null, true, "Failed to set remote description");
            }

            RTCSessionDescriptionAsyncOperation answerOp = connection.localConnection.CreateAnswer();

            await WaitUntilOperationIsDone(answerOp);

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.LogWarning("Operation was cancelled after creating answer.");
                return new OfferAnswer("", null, true, "Operation was cancelled after creating answer");
            }

            if (answerOp.IsError)
            {
                InstanceFinder.NetworkManager.LogWarning("Failed to create answer");
                return new OfferAnswer("", null, true, "Failed to create answer");
            }

            RTCSessionDescription descAnswer = answerOp.Desc;

            var localDescOp = connection.localConnection.SetLocalDescription(ref descAnswer);

            await WaitUntilOperationIsDone(localDescOp);

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.Log("Operation was cancelled after setting local description.");
                return new OfferAnswer("", null, true, "Operation was cancelled after setting local description");
            }

            if (localDescOp.IsError)
            {
                InstanceFinder.NetworkManager.LogError("Failed to set local description");
                return new OfferAnswer("", null, true, "Failed to set local description");
            }

            string sdp = answerOp.Desc.sdp;

            try
            {
                await Task.Delay(candidateCollectDuration, token);
            }
            catch (TaskCanceledException)
            {
                InstanceFinder.NetworkManager.Log("Delay was cancelled.");
                return new OfferAnswer("", null, true, "Operation was cancelled during delay");
            }

            OfferAnswer respondObject = new OfferAnswer(sdp, connection.iceCandidates.Select(s => s.Candidate).ToArray());

            if (token.IsCancellationRequested)
            {
                InstanceFinder.NetworkManager.Log("Operation was cancelled before adding ICE candidates.");
                return new OfferAnswer("", null, true, "Operation was cancelled before adding ICE candidates");
            }

            foreach (var iceCandidate in offer.candidates)
            {
                var candidateInit = new RTCIceCandidateInit
                {
                    candidate = iceCandidate,
                    sdpMid = "0",
                    sdpMLineIndex = 0
                };
                connection.localConnection.AddIceCandidate(new RTCIceCandidate(candidateInit));
            }

            return respondObject;
        }

#endif



        public void OnApplicationQuit()
        {
            Shutdown();
        }

    }
}