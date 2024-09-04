using FishNet.Object;
using UnityEngine;
using UnityEngine.UI;

public class UltimateTTT_Slot : NetworkBehaviour
{
    public int slotID;

    public UltimateTTT_SubGame subGame;

    public GameObject X;
    public GameObject O;


    public void Start()
    {

        GetComponent<Button>().onClick.AddListener(() =>
            {
                Debug.Log($"Pressed slot {slotID} in grid {subGame.subGameIndex}");
                
                if(UltimateTTT_NetworkManager._playerTurn != UltimateTTT.playerType)
                {
                    Debug.Log($"But it's not our turn!");

                    return;
                }

                if (UltimateTTT.status != GameStatus.InPlay) 
                {
                    //someones won or its draw, dont allow

                    return;
                }

                bool success = subGame.SlotSelected(slotID, UltimateTTT.playerType);
                if (success)
                {
                    if (UltimateTTT.playerType == SlotOption.X)
                    {
                        X.SetActive(true);
                    }
                    else
                    {
                        O.SetActive(true);
                    }
                    //UltimateTTT.temporaryPlayerChange();
                    Server_UpdateSlotSelection();
                }

            });
    }

    [ServerRpc(RequireOwnership = false)]
    public void Server_UpdateSlotSelection()
    {
        Client_UpdateSlotSelection(UltimateTTT_NetworkManager._playerTurn);


    }

    [ObserversRpc]
    public void Client_UpdateSlotSelection(SlotOption slot)
    {
        subGame.SlotSelected(slotID, slot);

        if (slot == SlotOption.X)
        {
            X.SetActive(true);
        }
        else
        {
            O.SetActive(true);
        }



        if (slot == SlotOption.X)
        {
            UltimateTTT_NetworkManager._playerTurn = SlotOption.O;
        }
        else
        {
            UltimateTTT_NetworkManager._playerTurn = SlotOption.X;
        }
    }

}
