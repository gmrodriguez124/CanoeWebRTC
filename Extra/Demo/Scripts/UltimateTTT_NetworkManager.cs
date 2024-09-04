using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

public class UltimateTTT_NetworkManager : NetworkBehaviour
{

    public static SlotOption _playerTurn = SlotOption.O;



    public static UltimateTTT_NetworkManager Instance;
    void Start()
    {
        Instance = this;
        InstanceFinder.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
    }





    private void ServerManager_OnRemoteConnectionState(NetworkConnection arg1, RemoteConnectionStateArgs arg2)
    {
        if (arg2.ConnectionState == RemoteConnectionState.Started)
        {
            if (arg1.ClientId != 0)
            {
                UltimateTTT_MainMenu.Instance.ActivateGameScreen();
            }
        }
        else
        {
            Debug.Log("player left");
        }
    }


}
