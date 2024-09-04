using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UltimateTTT_MainMenu : MonoBehaviour
{
    public Button HostGameButton;
    public Button JoinGameButton;
    public TMP_InputField roomCodeInputField;

    public TMP_Text RoomCode;

    public CanvasGroup mainMenuGroup;
    public CanvasGroup gameGroup;


    public GameObject waitMenu;
    public TMP_Text waitText;


    public static UltimateTTT_MainMenu Instance;
    
    private void Start()
    {
        Instance = this;

        HostGameButton.onClick.AddListener(() =>
        {
            waitMenu.SetActive(true);
            waitText.text = "Creating Game..";
            HostGame();
        });

        SignalManager.RoomCreatedCallback += (roomCode) =>
        {
            RoomCode.text = roomCode;
            JoinGame(roomCode);
        };

        JoinGameButton.onClick.AddListener(() =>
        {
            waitMenu.SetActive(true);
            waitText.text = "Joining Game..";
            JoinGame(roomCodeInputField.text);
        });

        SignalManager.JoinRoomCallback += (b) =>
        {
            if (b)
            {
                //joining now
                waitText.text = "Join code valid, connecting";
            }
            else
            {
                waitText.text = "Join code invalid, going back to menu";
                waitMenu.SetActive(false);
            }
        };

        InstanceFinder.ServerManager.OnServerConnectionState += (e) =>
        {
            if (e.ConnectionState == FishNet.Transporting.LocalConnectionState.Started)
            {
                SignalManager.CreateRoom();
            }
        };

        InstanceFinder.ClientManager.OnClientConnectionState += (e) =>
        {
            

            if (e.ConnectionState == FishNet.Transporting.LocalConnectionState.Started)
            {

                if (InstanceFinder.IsServerStarted)
                {
                    RoomCode.gameObject.SetActive(true);
                    waitText.text = "Waiting for other player to join...";

                    return;
                }

                ActivateGameScreen();
            }
            else if (e.ConnectionState == FishNet.Transporting.LocalConnectionState.Starting)
            {
                
            }
            else if (e.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped)
            {
                waitMenu.SetActive(false);


                gameGroup.alpha = 0;
                gameGroup.interactable = false;
                gameGroup.blocksRaycasts = false;

                mainMenuGroup.alpha = 1;
                mainMenuGroup.interactable = true;
                mainMenuGroup.blocksRaycasts = true;
            }
        };

    }

    public void ActivateGameScreen()
    {
        waitMenu.SetActive(false);

        gameGroup.alpha = 1;
        gameGroup.interactable = true;
        gameGroup.blocksRaycasts = true;

        mainMenuGroup.alpha = 0;
        mainMenuGroup.interactable = false;
        mainMenuGroup.blocksRaycasts = false;
    }

    public void HostGame()
    {

        InstanceFinder.ServerManager.StartConnection();
        InstanceFinder.ClientManager.StartConnection();

    }

    public void JoinGame(string roomCode)
    {
        InstanceFinder.ClientManager.StartConnection();

        SignalManager.JoinRoom(roomCode);
    }

}
