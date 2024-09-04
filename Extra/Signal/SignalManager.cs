using System.Text;
using System;
using FishNet;
using JamesFrowen.SimpleWeb;
using System.Linq;
using FishNet.Transporting.CanoeWebRTC; //this is only for the custombytearraypool!
using UnityEngine;

public class SignalManager : MonoBehaviour
{
    public string SignalAddress = "ws://localhost:9001/Signal";
    private string roomCode = "";

    public static Action<string> RoomCreatedCallback;
    public static Action<bool> JoinRoomCallback;



    const byte createRoom = 0x01; //responded to directly with the room code
    const byte attemptToJoinRoom = 0x02; //responded to directly if join code is valid and if host has been notified
    const byte joinRoomCallback = 0x03; //this is the callback for the client who initiated the attempt
    const byte receivedOfferFromHost = 0x04; //may contain error details if not allowed!
    const byte receivedAnswerFromClient = 0x05; //client has received the offer, has started to join, and is sending answer
    const byte trickleICE = 0x06; //not implemented
    const byte ping = 0x07;


    public static SimpleWebClient client;

    private void Start()
    {
        StartSignalClient();
    }


    void StartSignalClient()
    {
        var tcpConfig = new TcpConfig(noDelay: false, sendTimeout: 120000, receiveTimeout: 120000);
        client = SimpleWebClient.Create(ushort.MaxValue, 5000, tcpConfig);

        client.onConnect += () => Debug.Log($"<color=cyan>[Signal]</color> Connected");
        client.onDisconnect += () =>
        {
            Debug.Log($"<color=cyan>[Signal]</color> Disconnected");
            CanoeWebRTC.SignalShutdown();
        };
        client.onData += HandleReceivedData;
        client.onError += (exception) =>
        {
            Debug.Log($"<color=cyan>[Signal]</color> Error:{exception}");
            CanoeWebRTC.SignalShutdown();
        };



        
        client.Connect(new Uri(SignalAddress));

    }

    private void OnApplicationQuit()
    {
        client.Disconnect();
        client = null;
    }

    private static readonly ArraySegment<byte> createRoomSegment = new ArraySegment<byte>(new byte[] { createRoom });
    public static void CreateRoom()
    {
        if (InstanceFinder.IsServerStarted)
        {
            client.Send(createRoomSegment);
        }
        else
        {
            Debug.Log($"<color=cyan>[Signal]</color> Cannot create a room unless server is started");
        }
    }

    private static readonly byte[] joinRoomBuffer = new byte[10];
    public static void JoinRoom(string roomID)
    {
        Debug.Log($"<color=cyan>[Signal]</color> Attempting to join room <b><i><color=#DDA0DD>{roomID}</color></i></b>");

        byte roomIDLength = (byte)roomID.Length;

        int messageLength = 1 + 1 + roomIDLength;

        joinRoomBuffer[0] = attemptToJoinRoom;
        joinRoomBuffer[1] = roomIDLength;
        Encoding.UTF8.GetBytes(roomID, 0, roomID.Length, joinRoomBuffer, 2);

        client.Send(joinRoomBuffer);
    }


    //SERVER

    //Client has joined the room validly, lets create an offer and then send it
    public async void CreateAndSendOffer(int SignalID)
    {
        int connectionID = CanoeWebRTC.CreateNewRemoteConnection();


        OfferAnswer offerResult = await CanoeWebRTC.CreateOfferForClient(connectionID);


        if (offerResult.error)
        {
            Debug.Log($"<color=cyan>[Signal]</color> Error when creating offer: {offerResult.errorMessage}");
        }
        else
        {
            client.Send(sendOfferSerializer(FilterOutLocalandIPV6Candidates((OfferAnswer)offerResult), SignalID, connectionID));
        }
        
    }

    public static ArraySegment<byte> sendOfferSerializer(OfferAnswer offerAnswer, int SignalID, int connectionID)
    {



        byte[] requestObjectBytes = offerAnswer.Serialize();

        int totalLength = sizeof(byte) + sizeof(int) + sizeof(int) + requestObjectBytes.Length;

        byte[] result = CustomByteArrayPool.Retrieve(totalLength);

        int offset = 0;
        result[offset] = receivedOfferFromHost;
        offset += sizeof(byte);

        BitConverter.GetBytes(SignalID).CopyTo(result, offset);
        offset += sizeof(int);

        BitConverter.GetBytes(connectionID).CopyTo(result, offset);
        offset += sizeof(int);

        Array.Copy(requestObjectBytes, 0, result, offset, requestObjectBytes.Length);

        ArraySegment<byte> segment = new ArraySegment<byte>(result);
        return segment;
    }


    //client has sent us their response! (answer to our offer)
    public async void HandleResponse(int connectionID, OfferAnswer offerAnswer)
    {
        //Debug.Log("Awaiting the handle response functionality.. the server response is:");
        //Debug.Log(offerAnswer.sdp);
        //Debug.Log(offerAnswer.candidates);
        await CanoeWebRTC.HandleAnswerFromClient(connectionID, offerAnswer);
    }

    //Client

    //handle offer, generate reponse, send response
    public async void ReceivedOfferFromServer(int signalID, OfferAnswer offerAnswer)
    {
        OfferAnswer answerResult = await CanoeWebRTC.CreateAnswerForServer(offerAnswer);


        if (answerResult.error)
        {
            Debug.Log($"<color=cyan>[Signal]</color> Error when handling offer from server: {answerResult.errorMessage}");

        }
        else
        {





            client.Send(sendAnswerSerializer(FilterOutLocalandIPV6Candidates((OfferAnswer)answerResult), signalID));
        }

    }

    public static ArraySegment<byte> sendAnswerSerializer(OfferAnswer offerAnswer, int targetPlayerID)
    {
        byte[] respondObjectBytes = offerAnswer.Serialize();

        int totalLength = sizeof(byte) + sizeof(int) + respondObjectBytes.Length;
        byte[] result = CustomByteArrayPool.Retrieve(totalLength);

        int offset = 0;
        result[offset] = receivedAnswerFromClient;
        offset += sizeof(byte);

        BitConverter.GetBytes(targetPlayerID).CopyTo(result, offset);
        offset += sizeof(int);
        Array.Copy(respondObjectBytes, 0, result, offset, respondObjectBytes.Length);

        ArraySegment<byte> segment = new ArraySegment<byte>(result);
        return segment;
    }





    private OfferAnswer FilterOutLocalandIPV6Candidates(OfferAnswer offer)
    {
        if (!CanoeWebRTC._disableLocalConnections)
        {
            return offer;
        }

        var filteredCandidates = offer.candidates
            .Where(candidate =>
            {
                var parts = candidate.Split(' ');
                if (parts.Length < 5) return false;

                var address = parts[4];
                return !candidate.Contains("typ host") && (IsIPv4(address) || IsIPv6(address));
            })
            .ToArray();


        var filteredSdpLines = offer.sdp
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Where(line =>
            {
                if (line.StartsWith("a=candidate"))
                {
                    var parts = line.Split(' ');
                    if (parts.Length < 5) return true; 

                    var address = parts[4];

                    if (line.Contains("typ host") || !(IsIPv4(address) || IsIPv6(address)))
                    {
                        return false;
                    }
                }

                return true;
            })
            .ToArray();


        var filteredSdp = string.Join("\r\n", filteredSdpLines);


        return new OfferAnswer
        {
            sdp = filteredSdp,
            candidates = filteredCandidates
        };
    }


    public static bool IsIPv4(string address)
    {

        var octets = address.Split('.');
        return octets.Length == 4 && octets.All(octet => byte.TryParse(octet, out _));
    }

    public static bool IsIPv6(string address)
    {
        if (address.Contains('.'))
        {
            return false;
        }

        var parts = address.Split(':');

        int doubleColonCount = address.Split(new[] { "::" }, StringSplitOptions.None).Length - 1;
        if (doubleColonCount > 1)
        {
            return false;
        }

        var partsList = parts.Where(part => !string.IsNullOrEmpty(part)).ToList();

        if (partsList.Count > 8 || (doubleColonCount == 0 && partsList.Count != 8))
        {
            return false;
        }

        return true;
    }



    public void HandleReceivedData(ArraySegment<byte> data)
    {
        byte command = data.Array[data.Offset];

        switch (command)
        {
            case createRoom:
                
                int roomIDLength = data.Array[data.Offset + 1];

                string roomID = Encoding.UTF8.GetString(data.Array, data.Offset + 2, roomIDLength);

                roomCode = roomID;

                RoomCreatedCallback?.Invoke(roomID);

                Debug.Log($"<color=cyan>[Signal]</color> Room <b><i><color=#DDA0DD>{roomID}</color></i></b> created");
                GUIUtility.systemCopyBuffer = roomCode;
                break;

            case attemptToJoinRoom:
                // this is the host
                // a client is trying to join our game!
                // this is where we would initiate the create offer and then send

                Debug.Log("<color=cyan>[Signal]</color> Received request to join room, creating an offer");

                int clientAttemptingToJoin_SignalID = BitConverter.ToInt32(data.Array, data.Offset + 1);
                CreateAndSendOffer(clientAttemptingToJoin_SignalID);

                break;

            case joinRoomCallback:

                byte confirmation = data.Array[data.Offset + 1];

                if (confirmation == 0x00)
                {
                    //we did not join
                    Debug.Log("<color=cyan>[Signal]</color> Join Code Invalid");
                    JoinRoomCallback?.Invoke(false);
                }
                else if (confirmation == 0x01)
                {
                    //we did join
                    JoinRoomCallback?.Invoke(true);
                    Debug.Log("<color=cyan>[Signal]</color> Join Code Valid, waiting for offer from host");
                }


                break;

            case receivedOfferFromHost:
                // We should make an answer and respond

                Debug.Log("<color=cyan>[Signal]</color> Received an offer from a Host, creating an answer");


                int receivedOffer_HostSignalID = BitConverter.ToInt32(data.Array, data.Offset + 1);

                byte[] sendOffer_remainingData = data.Array.Skip(data.Offset + 5).Take(data.Count - 5).ToArray();

                //create an answer and send it back to them
                OfferAnswer offer = OfferAnswer.Deserialize(sendOffer_remainingData);

                ReceivedOfferFromServer(receivedOffer_HostSignalID, offer);

                break;

            case receivedAnswerFromClient:
                // server received an answer from the client
                // need to handle their answer

                Debug.Log("<color=cyan>[Signal]</color> Client sent us their answer");

                int connID = BitConverter.ToInt32(data.Array, data.Offset + 1);

                byte[] sendAnswer_remainingData = data.Array
                    .Skip(data.Offset + 5)
                    .Take(data.Count - 5)
                    .ToArray();

                HandleResponse(connID, OfferAnswer.Deserialize(sendAnswer_remainingData));

                break;

            case trickleICE:
                // NOT IMPLEMENTED

                int trickleICE_SenderSignalID = BitConverter.ToInt32(data.Array, data.Offset + 1);

                byte[] sendICE_remainingData = data.Array.Skip(data.Offset + 5).Take(data.Count - 5).ToArray();


                break;
            
            case ping:
                //simple ping
                client.Send(data);
                break;
            
            default:
                throw new InvalidOperationException("Unknown command received");
        }
    }

    public void Update()
    {
        if (client != null)
        {
            client.ProcessMessageQueue();
        }
    }
}



    