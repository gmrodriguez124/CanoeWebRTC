# Canoe WebRTC

A WebRTC transport for use with [FishNetworking](https://github.com/FirstGearGames/FishNet)

Inspired by and based on [Bayou](https://github.com/FirstGearGames/Bayou/tree/main), [FishyWebRTC](https://github.com/cakeslice/FishyWebRTC), [SimpleWebTransport](https://github.com/James-Frowen/SimpleWebTransport), [Tugboat](https://github.com/FirstGearGames/FishNet/tree/main/Assets/FishNet/Runtime/Transporting/Transports/Tugboat), and likely more.

### **Supported**
- WebGL and Standalone (cross support as well)
  - Standalone - Unity-WebRTC needs to support the platform
  - WebGL - dependant on browser support (tested on Edge, Chrome, Firefox)
- Host, Client, and Server
- Unreliable and Reliable using UDP
- Partial P2P
  - P2P with Host - not between clients
- Exposed Signalling Functionality
  - See [Creating Your Own Signalling](#creating-your-own-signalling)


### **Limitations**
- No ICE trickling beyond the initial collection as handled by the `Candidate Collect Duration` variable
  - Not too difficult to implement but I've personally found that all ICE Candidates are gathered in less than ~0.5s including STUN servers
- Possibly more that I do not currently remember (if you find any, please let me know)

### **Requirements**
- [FishNetworking](https://github.com/FirstGearGames/FishNet)
- [Unity WebRTC Package](https://docs.unity3d.com/Packages/com.unity.webrtc@3.0/manual/index.html) (Specifically v3.0.0-pre.7)
  - `com.unity.webrtc@3.0.0-pre.7`

### **Installation**
Download and install FishNet and Unity WebRTC.

Download and import the [latest CanoeWebRTC package](https://github.com/gmrodriguez124/CanoeWebRTC/releases/latest) 
(tested in Unity 2022.3)

The package comes with two extras, a signal manager and a very poorly written demo game (Ultimate Tic Tac Toe) - I highly recommend against using this as an example, it was written very quickly and dirty.        
Read [Signalling](#signalling)
below for details on the signal manager and an included signal server.

### **Usage**
**CanoeWebRTC Component Variables**
- `ICE Servers`
- `Filter Local Connections`
  - Does ***NOT*** disable the *gathering* of local ICE candidates but attempts to filter local ICE Candidates before outputting the Offer/Answer
    - Will force Hosts (server / client) to use STUN or TURN since there is no differentiation of a client connecting to itself implemented
- `Only Allow Relay`
  - Sets IceTransportPolicy to 'Relay' (untested, if you verify please let me know)
- `Operation Timeout Duration` (Signalling)
  - Specifies how long each signal operation (creating an offer, handling an offer/creating an answer, handling an answer) has before timing out and producing an error - only applies to standalone. WebGL has error handling through javascript and callbacks (see [Creating Your Own Signalling](#creating-your-own-signalling))
- `Candidate Collect Duration` (Signalling)
  - Specifies how long to wait for ICE candidates to collect before returning the offer / answer (trickling of ICE candidates is not implemented)
- `Signal Timeout Duration` (Signalling)
  - Specifies how long the ENTIRE signalling process has before timing out.
    - For the server, starts when creating an offer (`CreateOfferForClient`) and is finished when an answer has been received (`HandleAnswerFromClient`)
    - For the client, starts when creating an answer (`CreateAnswerForServer`) and is finished when fully connected
  
Before creating an offer, you need to start the server as specified in the FishNetworking documentation.

Before handling an offer (and creating an answer) you need to start the client as specified in the FishNetworking documentation.

A Host is just a server with a client that connects to itself. A local connection is typically used but if disabled (through the browser, through the exposed variable, something else) then it will use stun or turn servers (potentially wasting bandwidth).

## **Signalling**

WebRTC between two WebGL peers requires signalling. I recommend researching more independently if you are not sure what this is.

Included in the source code and/or unity package is a Signal Manager which was written to work specifically with the WebSocket signal server provided in /Extra/Signal/SignalServer.js - or [here](https://github.com/gmrodriguez124/CanoeWebRTC/tree/main/Extra/Signal). Requires [uWebSocket.js](https://github.com/uNetworking/uWebSockets.js) and [Node.js](https://nodejs.org/en).

The unity package ***does NOT*** include this signal server but ***does*** include the Signal Manager.

The Signal Manager utilizes the [SimpleWebTransport by James Frowen](https://github.com/James-Frowen/SimpleWebTransport) which is included in the unity package - feel free to exclude the included one and download the latest release

Provided signal manager and server features:
- Room / code join functionality
- Auto routing of offer / answer between clients and hosts/servers

---
This meets most of my needs and is extensible but I *highly* recommend writing your own with specific functionality for your project. CanoeWebRTC contains offer / answer 'access points' which the aforementioned Signal Manager also uses.

### **Creating Your Own Signalling**

Offers are created on the server and sent to clients:
        
    int connectionID = CanoeWebRTC.CreateNewRemoteConnection();
    OfferAnswer offerResult = await CanoeWebRTC.CreateOfferForClient(connectionID);

    //Typically initiated by the client requesting to join through signalling

Once the offer is received on the client, they need to be handled and an answer created and sent back:

    OfferAnswer answerResult = await CanoeWebRTC.CreateAnswerForServer(offerResult);

    //Will start the connection to the server

Then when the answer is received on the server, it needs to be handled:

    await CanoeWebRTC.HandleAnswerFromClient(connectionID, offerAnswer);

    //Has no callbacks
    //Will start the connection to the client

OfferAnswer has an error and message property which you should check before sending off. It is handled in the backend automatically (connections are closed). 


## **DISCLAIMER**
This plugin is provided as-is and is intended for use at your own risk. It is crucial that you thoroughly test this plugin in your specific environment before deploying it in any production setting. By downloading and using this plugin, you agree that you are solely responsible for any modifications, implementations, or outcomes that may result from its use. Please ensure that all necessary backups are made, and that any testing is conducted in a safe environment. The developer is not responsible for any direct or indirect consequences, including but not limited to, data loss, damage, or any other effects that may occur as a result of using this plugin.

This is a hobby project for me, and I am not an expert in the related fields. Please do your due diligence before using in production.
