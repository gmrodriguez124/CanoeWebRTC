
const RemoteConnections = {};

const ClientConnection = null;

class Connection {
   constructor() {
      this.closing = false;
      this.connectionID = -1;
      this.peerConnection = null;
      this.channelsOpened = 0;
      this.local_unreliableDataChannel = null;
      this.local_reliableDataChannel = null;
      this.remote_unreliableDataChannel = null;
      this.remote_reliableDataChannel = null;
      this.iceCandidates = [];
    }
 }

 const MemoryManager = {
   pool: [],
   maxPoolSize: 250,
 
   grab: function (array) {
     let size = array.length;
     let allocatedPtr;
 
     if (this.pool.length > 0) {
       let lastBlock = this.pool[this.pool.length - 1];
 
       if (lastBlock.size >= size) {
         let block = this.pool.pop();
         allocatedPtr = block.ptr;
 
         if (block.size > size) {
           let leftoverBlock = {
             ptr: allocatedPtr + size,
             size: block.size - size,
           };
           this.done(leftoverBlock.ptr, leftoverBlock.size);
         }
       } else {
         if (this.pool.length >= this.maxPoolSize) {
           let blockToFree = this.pool.pop();
           _free(blockToFree.ptr);
         }
         allocatedPtr = _malloc(size);
       }
     } else {
       allocatedPtr = _malloc(size);
     }
 
     HEAPU8.set(array, allocatedPtr);
 
     return { ptr: allocatedPtr, length: size };
   },
 
   done: function (ptr, size) {
     if (this.pool.length < this.maxPoolSize) {
       this.pool.push({ ptr, size });
     } else {
       _free(ptr);
     }
   },
 
   clean: function () {
     for (let block of this.pool) {
       _free(block.ptr);
     }
     this.pool = [];
   },
 };
 


 let IceServers = []; 

 function RegisterICEServers(iceServers) {
   const iceServersString = UTF8ToString(iceServers);
   
   if (!iceServersString || iceServersString.trim() === "") {
       IceServers = [];
   } else {
       IceServers = iceServersString.split(";;").map(entry => {
           const [url, username, credential] = entry.split("__");

           if (url && url.trim() !== "") {
               const server = { urls: url };
               if (username && credential) {
                   server.username = username;
                   server.credential = credential;
               }
               return server;
           }

           return null; 
       }).filter(server => server !== null);
   }

}




function RegisterClientCallbacks(
    remoteChannelClosedCallback_Client,
    remoteChannelOpenedCallback_Client,
    reliableMessageReceivedCallback_Client,
    unreliableMessageReceivedCallback_Client,
    respondToOfferCallback,
    candidateCollectDuration,
    onlyAllowRelay
 ) {
    var unity_remoteChannelClosedCallback_Client = {{{ makeDynCall('v', 'remoteChannelClosedCallback_Client') }}};
    RemoteChannelsClosed_Client = function () {
      if (ClientConnection){
         ClientConnection.closing = true;
      
      unity_remoteChannelClosedCallback_Client();
      }
    };
 
    var unity_remoteChannelOpenedCallback_Client = {{{ makeDynCall('v', 'remoteChannelOpenedCallback_Client') }}};
    RemoteChannelsOpened_Client = function () {
       unity_remoteChannelOpenedCallback_Client();
    };
 
    var unity_reliableMessageReceivedCallback_Client = {{{ makeDynCall('vii', 'reliableMessageReceivedCallback_Client') }}};
    ReceivedReliableMessage_Client = function (event) {
       handleReceivedMessage_Client(event, unity_reliableMessageReceivedCallback_Client);
    };
 
    var unity_unreliableMessageReceivedCallback_Client = {{{ makeDynCall('vii', 'unreliableMessageReceivedCallback_Client') }}};
    ReceivedUnreliableMessage_Client = function (event) {
       handleReceivedMessage_Client(event, unity_unreliableMessageReceivedCallback_Client);
    };

    var unity_respondToOfferCallback = {{{ makeDynCall('vi', 'respondToOfferCallback') }}};
    RespondToOfferCallback = function (jsonString) {
       unity_respondToOfferCallback(jsonString);
    };
 
    function handleReceivedMessage_Client(event, unityCallback) {       
         if (event.data instanceof ArrayBuffer) {
            let array = new Uint8Array(event.data);

            let { ptr, length } = MemoryManager.grab(array);
            unityCallback(ptr, length);
            MemoryManager.done(ptr, length);

         } else if (event.data instanceof Blob) {
               // firefox sends blobs instead of arraybuffers
               let reader = new FileReader();
               reader.onload = function() {
                  let arrayBuffer = reader.result;
                  let array = new Uint8Array(arrayBuffer);

                  let { ptr, length } = MemoryManager.grab(array);
                  unityCallback(ptr, length);
                  MemoryManager.done(ptr, length);
               };
               reader.readAsArrayBuffer(event.data);

         } else {
               console.error("Message type not supported");
         }
    }

    CandidateCollectDuration = candidateCollectDuration;
    OnlyAllowRelay = onlyAllowRelay;

 }
 
function RegisterServerCallbacks(
    remoteChannelClosedCallback_Server,
    remoteChannelOpenedCallback_Server,
    reliableMessageReceivedCallback_Server,
    unreliableMessageReceivedCallback_Server,
    createOfferCallback,
    candidateCollectDuration,
    onlyAllowRelay
 ) {
    var unity_remoteChannelClosedCallback_Server = {{{ makeDynCall('vi', 'remoteChannelClosedCallback_Server') }}};
    RemoteChannelsClosed_Server = function (connectionID) {
      if(RemoteConnections[connectionID]){
         RemoteConnections[connectionID].closing = true;
         unity_remoteChannelClosedCallback_Server(connectionID);
      }
    };
 
    var unity_remoteChannelOpenedCallback_Server = {{{ makeDynCall('vi', 'remoteChannelOpenedCallback_Server') }}};
    RemoteChannelsOpened_Server = function (connectionID) {
       unity_remoteChannelOpenedCallback_Server(connectionID);
    };
 
    var unity_reliableMessageReceivedCallback_Server = {{{ makeDynCall('viii', 'reliableMessageReceivedCallback_Server') }}};
    ReceivedReliableMessage_Server = function (event, connectionID) {
       handleReceivedMessage_Server(event, connectionID, unity_reliableMessageReceivedCallback_Server);
    };
 
    var unity_unreliableMessageReceivedCallback_Server = {{{ makeDynCall('viii', 'unreliableMessageReceivedCallback_Server') }}};
    ReceivedUnreliableMessage_Server = function (event, connectionID) {
       handleReceivedMessage_Server(event, connectionID, unity_unreliableMessageReceivedCallback_Server);
    };
    
    var unity_createOfferCallback = {{{ makeDynCall('vii', 'createOfferCallback') }}};
    CreateOfferCallback = function (connectionID, jsonString) {
       unity_createOfferCallback(connectionID, jsonString);
    };

    function handleReceivedMessage_Server(event, connectionID, unityCallback) {
       
 
         if (event.data instanceof ArrayBuffer) {
               let array = new Uint8Array(event.data);
      
               let { ptr, length } = MemoryManager.grab(array);
               unityCallback(connectionID, ptr, length);
               MemoryManager.done(ptr, length);
      
         } else if (event.data instanceof Blob) {
               // firefox
               let reader = new FileReader();
               reader.onload = function() {
                  let arrayBuffer = reader.result;
                  let array = new Uint8Array(arrayBuffer);
      
                  let { ptr, length } = MemoryManager.grab(array);
                  unityCallback(connectionID, ptr, length);
                  MemoryManager.done(ptr, length);
               };
               reader.readAsArrayBuffer(event.data);
      
         } else {
               console.error("Message type not supported");
         }
    }

    CandidateCollectDuration = candidateCollectDuration;
    OnlyAllowRelay = onlyAllowRelay;
 }




function CreateClientConnection() {


   if (window.adapter) {
      console.log("WebRTC Adapter is available and can be used.");
    } else {
      console.log("WebRTC Adapter is not available.");
    }


   if (ClientConnection != null) {
      console.error("[Client] Can't create another client, one already exists.");
      return;
   }

   let newConn = new Connection();

   newConn.connectionID = -1;


   newConn.peerConnection = new RTCPeerConnection({
      iceTransportPolicy: (OnlyAllowRelay === 1 ? 'relay' : 'all'),
      iceServers: IceServers
   });

   newConn.local_unreliableDataChannel = newConn.peerConnection.createDataChannel("Unreliable", {
      ordered: false,
      maxRetransmits: 0
   });

   newConn.local_reliableDataChannel = newConn.peerConnection.createDataChannel("Reliable", {
      ordered: true
   });

   newConn.local_reliableDataChannel.onerror = function (event) {
      console.error(`ERROR - Local Reliable Channel\nType: ${event.type} Message: ${event.message}`);
      
      if(!newConn.closing){
         RemoteChannelsClosed_Client();
      }
   };
   

   newConn.local_unreliableDataChannel.onerror = function (event) {
      console.error(`ERROR - Local Unreliable Channel\nType: ${event.type} Message: ${event.message}`);
      if(!newConn.closing){
         RemoteChannelsClosed_Client();
      }
   };

   newConn.peerConnection.ondatachannel = function (event) {
      const channel = event.channel;

      if (channel.label === "Unreliable") {
         //console.log("Remote Unreliable Channel received");
         newConn.remote_unreliableDataChannel = channel;

         channel.onmessage = function(event) {
            ReceivedUnreliableMessage_Client(event);
         };

         channel.onerror = function (event) {
            console.error("Error... Remote Unreliable Channel", event);
            if(!newConn.closing){
               RemoteChannelsClosed_Client();
            }
         };

         channel.onopen = function () {
            //console.log("Remote Unreliable Channel Opened");
         };

         channel.onclose = function () {
            //console.log("Remote Unreliable Channel Closed");
            if(!newConn.closing){
               RemoteChannelsClosed_Client();
            }
         };

      } else if (channel.label === "Reliable") {
         //console.log("Remote Reliable Channel received");
         newConn.remote_reliableDataChannel = channel;

         channel.onmessage = function(event) {
            ReceivedReliableMessage_Client(event);
         };

         channel.onerror = function (event) {
            console.error("Error... Remote Reliable Channel", event);
            if(!newConn.closing){
               RemoteChannelsClosed_Client();
            }
         };

         channel.onopen = function () {
            //console.log("Remote Reliable Channel Opened");
         };

         channel.onclose = function () {
            //console.log("Remote Reliable Channel Closed");
            if(!newConn.closing){
               RemoteChannelsClosed_Client();
            }
         };
      }

      newConn.channelsOpened++;
      if (newConn.channelsOpened === 2) {
         RemoteChannelsOpened_Client();
      }
   };

   newConn.peerConnection.onicecandidate = function (event) {
      if (event.candidate) {
         const candidate = event.candidate;

         if (candidate.protocol === "udp") {
            //console.log(`New local ICE candidate: ${candidate.candidate}`);
            newConn.iceCandidates.push(candidate);
         }
      }
   };

   newConn.peerConnection.oniceconnectionstatechange = function () {
      const state = newConn.peerConnection.iceConnectionState;
      //console.log(`ICE Connection State changed to: ${state}`);
   };

   newConn.peerConnection.onconnectionstatechange = function () {
      const state = newConn.peerConnection.connectionState;
      //console.log(`Peer Connection State changed to: ${state}`);
      
      if(state == 'disconnected' || state == 'failed' || state == 'closed'){
         //RemoteChannelsClosed_Client();
         if(!newConn.closing){
            RemoteChannelsClosed_Client();
         }
      }
      
   };

   ClientConnection = newConn;
}


function CreateRemoteConnection(connectionID) {

   if (window.adapter) {
      //console.log("WebRTC Adapter is available and can be used.");
    } else {
      //console.log("WebRTC Adapter is not available.");
    }

   let newConn = new Connection();

   newConn.connectionID = connectionID;


   newConn.peerConnection = new RTCPeerConnection({
      iceTransportPolicy: (OnlyAllowRelay === 1 ? 'relay' : 'all'),
      iceServers: IceServers
   });

   newConn.local_unreliableDataChannel = newConn.peerConnection.createDataChannel("Unreliable", {
      ordered: false,
      maxRetransmits: 0
   });

   newConn.local_reliableDataChannel = newConn.peerConnection.createDataChannel("Reliable", {
      ordered: true
   });

   newConn.local_reliableDataChannel.onerror = function (event) {
      console.error(`ERROR - Local Reliable Channel\nType: ${event.type} Message: ${event.message}`);
      if(!newConn.closing){
         RemoteChannelsClosed_Server(connectionID);
      }
   };

   newConn.local_unreliableDataChannel.onerror = function (event) {
      console.error(`ERROR - Local Unreliable Channel\nType: ${event.type} Message: ${event.message}`);
      if(!newConn.closing){
         RemoteChannelsClosed_Server(connectionID);
      }
   };

   newConn.peerConnection.ondatachannel = function (event) {
      const channel = event.channel;

      if (channel.label === "Unreliable") {
         //console.log("Remote Unreliable Channel received");
         newConn.remote_unreliableDataChannel = channel;

         channel.onmessage = function(event) {
            ReceivedUnreliableMessage_Server(event, connectionID);
         };

         channel.onerror = function (event) {
            console.error("Error... Remote Unreliable Channel", event);
            if(!newConn.closing){
               RemoteChannelsClosed_Server(connectionID);
            }
         };

         channel.onopen = function () {
            //console.log("Remote Unreliable Channel Opened");
         };

         channel.onclose = function () {
            //console.log("Remote Unreliable Channel Closed");
            if(!newConn.closing){
               RemoteChannelsClosed_Server(connectionID);
            }
         };

      } else if (channel.label === "Reliable") {
         //console.log("Remote Reliable Channel received");
         newConn.remote_reliableDataChannel = channel;

         channel.onmessage = function(event) {
            ReceivedReliableMessage_Server(event, connectionID);
         };

         channel.onerror = function (event) {
            console.error("Error... Remote Reliable Channel", event);
            if(!newConn.closing){
               RemoteChannelsClosed_Server(connectionID);
            }
         };

         channel.onopen = function () {
            //console.log("Remote Reliable Channel Opened");
         };

         channel.onclose = function () {
            //console.log("Remote Reliable Channel Closed");
            if(!newConn.closing){
               RemoteChannelsClosed_Server(connectionID);
            }
         };
      }

      newConn.channelsOpened++;
      if (newConn.channelsOpened === 2) {
         RemoteChannelsOpened_Server(connectionID);
      }
   };

   newConn.peerConnection.onicecandidate = function (event) {
      if (event.candidate) {
         const candidate = event.candidate;

         if (candidate.protocol === "udp") {
            //console.log(`ON SERVER -- New ICE candidate for remote connection: ${candidate.candidate}`);
            newConn.iceCandidates.push(candidate);
         }
      }
   };

   newConn.peerConnection.oniceconnectionstatechange = function () {
      const state = newConn.peerConnection.iceConnectionState;
      //console.log(`ICE Connection State changed to: ${state}`);
      //wont do anything here since below should handle
   };

   newConn.peerConnection.onconnectionstatechange = function () {
      const state = newConn.peerConnection.connectionState;
      //console.log(`Peer Connection State changed to: ${state}`);

      if(state == 'disconnected' || state == 'failed' || state == 'closed'){
         //RemoteChannelsClosed_Server(connectionID);
         if(!newConn.closing){
            RemoteChannelsClosed_Server(connectionID);
         }
      }
      
   };

   RemoteConnections[connectionID] = newConn;
}



function CloseClientConnection() {
   //console.log("Attempting to close client connection");

   if (ClientConnection != null) {
      ClientConnection.peerConnection.close();
      ClientConnection = null;
      MemoryManager.clean();
   } else {
      console.error("[Client] Trying to close client connection which doesn't exist.");
   }
}

//only from server
function GetRemoteConnectionState(connectionID){
    if(connectionID in RemoteConnections){
        //it exists
        if(RemoteConnections[connectionID].peerConnection.connectionState == 'connected'){
            return 1;
        }
    }

    return 0;
}

//only from server
function CloseRemoteConnection(connectionID) {
    //console.log("Attempting to close server connection ", connectionID);
    if (connectionID in RemoteConnections) {
      RemoteConnections[connectionID].peerConnection.close();
      RemoteConnections[connectionID] = null;

      delete RemoteConnections[connectionID];
    } else {
        console.error("[Server] Trying to close a connection that doesn't exist.");
    }
}

function CloseAllRemoteConnections() {

   //console.log("If server is not closing... something is wrong. we are closing all connections");
    for (let connectionID in RemoteConnections) {
      RemoteConnections[connectionID].peerConnection.close();
      RemoteConnections[connectionID] = null;

      delete RemoteConnections[connectionID];
        
    }
    MemoryManager.clean();

}

 

function SendUnreliable_ToServer(dataPtr, dataSize) {
   ////console.log("Attempting to send Unreliable");
   var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

   if(ClientConnection) {
      if(!ClientConnection.closing) {
         ClientConnection.local_unreliableDataChannel.send(dataArray);
      }else{
         console.error("Tried to send to closing connection");
      }
   }
   else {
      console.error("Tried to send to nonexistant connection");
   }
}

function SendUnreliable_ToClient(connectionID, dataPtr, dataSize) {
   ////console.log("Attempting to send Unreliable");
   var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

   if(RemoteConnections[connectionID]) {
      if(!RemoteConnections[connectionID].closing) {
         RemoteConnections[connectionID].local_unreliableDataChannel.send(dataArray);
      }else{
         console.error("Tried to send to closing connection");
      }
   }
   else {
      console.error("Tried to send to nonexistant connection");
   }
}

function SendUnreliable_ToAllClients(dataPtr, dataSize) {
    ////console.log("Attempting to send Unreliable to all clients");

    var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

    for (let connectionID in RemoteConnections) {
      if(RemoteConnections[connectionID]) {
         if(!RemoteConnections[connectionID].closing) {
            RemoteConnections[connectionID].local_unreliableDataChannel.send(dataArray);
         }else{
            console.error("Tried to send to closing connection");
         }
      }
      else {
         console.error("Tried to send to nonexistant connection");
      }
   }
}


function SendReliable_ToServer(dataPtr, dataSize) {
   ////console.log("Attempting to send Reliable");

   var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

   if(ClientConnection) {
      if(!ClientConnection.closing) {
         ClientConnection.local_reliableDataChannel.send(dataArray);
      }else{
         console.error("Tried to send to closing connection");
      }
   }
   else {
      console.error("Tried to send to nonexistant connection");
   }
}

function SendReliable_ToClient(connectionID, dataPtr, dataSize) {
   ////console.log("Attempting to send Reliable");

   var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

   if(RemoteConnections[connectionID]) {
      if(!RemoteConnections[connectionID].closing) {
         RemoteConnections[connectionID].local_reliableDataChannel.send(dataArray);
      }else{
         console.error("Tried to send to closing connection");
      }
   }
   else {
      console.error("Tried to send to nonexistant connection");
   }
}

function SendReliable_ToAllClients(dataPtr, dataSize) {
    ////console.log("Attempting to send Reliable to all clients");

    var dataArray = HEAPU8.subarray(dataPtr, dataPtr + dataSize);

    for (let connectionID in RemoteConnections) {
      if(RemoteConnections[connectionID]) {
         if(!RemoteConnections[connectionID].closing) {
            RemoteConnections[connectionID].local_reliableDataChannel.send(dataArray);
         }else{
            console.error("Tried to send to closing connection");
         }
      }
      else {
         console.error("Tried to send to nonexistant connection");
      }
    }
}


//create offer for a client from the server
function CreateOffer(connectionID) {
   //console.log("Create Offer Called");

   RemoteConnections[connectionID].iceCandidates = [];

   RemoteConnections[connectionID].peerConnection.createOffer().then(function (offer) {
      return RemoteConnections[connectionID].peerConnection.setLocalDescription(offer);
   }).then(function () {

         setTimeout(function () {

               var offerSdp = RemoteConnections[connectionID].peerConnection.localDescription.sdp;
               var iceCandidatesArray = RemoteConnections[connectionID].iceCandidates.map(candidate => candidate.candidate);

               var data = {
                  sdp: offerSdp,
                  candidates: iceCandidatesArray
               };

               var jsonString = JSON.stringify(data);
               ////console.log('sending back offer with candidates')
               CreateOfferCallback(connectionID, allocateUTF8(jsonString));

            //unity_offerCallback(allocateUTF8(offerSdp), SimpleWebRTC.iceCandidates.map(candidate => candidate.candidate));
         }, CandidateCollectDuration);

      }).catch(function (error) {
            console.error('Failed to create offer or set local description:', error);

            var erroredData = {
               sdp: "",
               candidates: [],
               error: true,
               errorMessage: "Failed to create offer or set local description: " + error
            };

            var erroredJsonString = JSON.stringify(erroredData);

            CreateOfferCallback(connectionID, allocateUTF8(erroredJsonString));

         });
}

//only done on server when receiving an answer from the client we sent an offer to
function HandleAnswer(connectionID, answerJSON) {
   var answerJSONHandled = UTF8ToString(answerJSON);
   var answerData = JSON.parse(answerJSONHandled);

   var answerSdp = answerData.sdp;

   var iceCandidatesArray = answerData.candidates;

   

   var answer = new RTCSessionDescription({
       type: 'answer',
       sdp: answerSdp
   });

   RemoteConnections[connectionID].peerConnection.setRemoteDescription(answer).then(function () {

         iceCandidatesArray.forEach(function (candidate) {

            //can likely just set to 0 instead of checking
            var iceCandidate = new RTCIceCandidate({ 
               candidate: candidate,
               sdpMid: candidate.sdpMid !== null && candidate.sdpMid !== undefined ? candidate.sdpMid : "0",
               sdpMLineIndex: candidate.sdpMLineIndex !== null && candidate.sdpMLineIndex !== undefined ? candidate.sdpMLineIndex : 0
            });

            RemoteConnections[connectionID].peerConnection.addIceCandidate(iceCandidate).catch(function (error) {
                  console.error('Failed to add ICE candidate:', error);
            });
         });

   }).catch(function (error) {
       console.error('Failed to set remote description:', error);
       RemoteConnections[connectionID].peerConnection.close(); //will cause callback
   });
}

//done on client when server sends us an offer
function HandleOffer(offerJSON) {
   //console.log("[Client] Handle Offer Called");

   var offerJSONHandled = UTF8ToString(offerJSON);

   var offerData = JSON.parse(offerJSONHandled);

   var offerSdp = offerData.sdp;
   var iceCandidatesArray = offerData.candidates;

   //console.log("The following are the candidates we received:");
   //console.log("----------------------------------------");
   //iceCandidatesArray.forEach(candidate => console.log(candidate));
   //console.log("----------------------------------------");

   var peerConnection = ClientConnection.peerConnection;

   var offer = new RTCSessionDescription({
       type: 'offer',
       sdp: offerSdp
   });

   peerConnection.setRemoteDescription(offer).then(function () {

       iceCandidatesArray.forEach(function (candidate) {
           var iceCandidate = new RTCIceCandidate({ 
            candidate: candidate,
            sdpMid: candidate.sdpMid !== null && candidate.sdpMid !== undefined ? candidate.sdpMid : "0",
            sdpMLineIndex: candidate.sdpMLineIndex !== null && candidate.sdpMLineIndex !== undefined ? candidate.sdpMLineIndex : 0
         });
           peerConnection.addIceCandidate(iceCandidate).catch(function (error) {
               console.error('Failed to add ICE candidate', error);
           });
       });

       return peerConnection.createAnswer();
   }).then(function (answer) {
       return peerConnection.setLocalDescription(answer);
      }).then(function () {
         setTimeout(function () {
            var answerSdp = peerConnection.localDescription.sdp;
            var gatheredIceCandidates = ClientConnection.iceCandidates.map(candidate => candidate.candidate);

            //console.log("The following is what we generated!");
            //console.log("----------------------------------------");
            //gatheredIceCandidates.forEach(candidate => console.log(candidate));
            //console.log("----------------------------------------");

            var responseData = {
                  sdp: answerSdp,
                  candidates: gatheredIceCandidates
            };

            var jsonResponseString = JSON.stringify(responseData);
            
            RespondToOfferCallback(allocateUTF8(jsonResponseString));

            
         }, CandidateCollectDuration);
         }).catch(function (error) {
            console.error('Failed to handle server offer', error);
            var erroredData = {
               sdp: "",
               candidates: [],
               error: true,
               errorMessage: "Failed to handle server offer: " + error
            };

      var erroredJsonString = JSON.stringify(erroredData);

      RespondToOfferCallback(allocateUTF8(erroredJsonString));
      });
}

function ClientAcknowledged() {
   
}

const WebGLWebRTCLib = {
   $Connection: Connection,
   $ClientConnection: ClientConnection,
   $RemoteConnections: RemoteConnections,
   $MemoryManager: MemoryManager,
   $IceServers: IceServers,
   RegisterICEServers,
   RegisterClientCallbacks,
   RegisterServerCallbacks,
   CreateClientConnection,
   CreateRemoteConnection,
   CloseClientConnection,
   GetRemoteConnectionState,
   CloseRemoteConnection,
   CloseAllRemoteConnections,
   SendUnreliable_ToServer,
   SendUnreliable_ToClient,
   SendUnreliable_ToAllClients,
   SendReliable_ToServer,
   SendReliable_ToClient,
   SendReliable_ToAllClients,
   CreateOffer,
   HandleAnswer,
   HandleOffer
};

autoAddDeps(WebGLWebRTCLib, "$Connection");
autoAddDeps(WebGLWebRTCLib, "$ClientConnection");
autoAddDeps(WebGLWebRTCLib, "$RemoteConnections");
autoAddDeps(WebGLWebRTCLib, "$MemoryManager");
autoAddDeps(WebGLWebRTCLib, "$IceServers");
mergeInto(LibraryManager.library, WebGLWebRTCLib);
