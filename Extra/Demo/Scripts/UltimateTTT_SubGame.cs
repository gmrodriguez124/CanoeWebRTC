using System;
using UnityEngine;

public class UltimateTTT_SubGame : MonoBehaviour
{

    public int subGameIndex;

    public SlotOption[] slots = new SlotOption[9]
    {
        SlotOption.None,
        SlotOption.None,
        SlotOption.None,
        SlotOption.None,
        SlotOption.None,
        SlotOption.None,
        SlotOption.None,
        SlotOption.None,
        SlotOption.None
    };

    public GameStatus status = GameStatus.InPlay;

    public Action<int, int, SlotOption> OnSubGameSlotChange;
    public Action<int, GameStatus> OnSubGameStatusChange;


    public GameObject[] WinGrids = new GameObject[8];

    public GameObject BackGround;
    public GameObject O;
    public GameObject X;

    public bool SlotSelected(int slotIndex, SlotOption slotSelector)
    {
        if (UltimateTTT.currentGridPlayIndex != -1)
        {
            if (UltimateTTT.currentGridPlayIndex != subGameIndex)
            {
                Debug.Log("Attempted to play in slot that is not currently in play");
                return false;
            }
        }

        if (slots[slotIndex] == SlotOption.None && status == GameStatus.InPlay)
        {
            
            slots[slotIndex] = slotSelector;
            OnSubGameSlotChange?.Invoke(subGameIndex, slotIndex, slotSelector);
        }
        else if (status != GameStatus.InPlay)
        {
            Debug.Log("Tried to play in a grid that has already been decided");
            return false;
        }
        else if (slots[slotIndex] != SlotOption.None)
        {
            Debug.Log("Tried to play in a slot that has already been taken");
            return false;
        }

        (GameStatus gameStatus, int winCondition) = UltimateTTT_WinConditions.CheckSubGameStatus(slots);

        status = gameStatus;

        switch (gameStatus)
        {
            case GameStatus.X:
                BackGround.SetActive(true);
                X.SetActive(true);
                WinGrids[winCondition].SetActive(true);
                OnSubGameStatusChange?.Invoke(subGameIndex, gameStatus);
                break;
            case GameStatus.O:
                BackGround.SetActive(true);
                O.SetActive(true);
                WinGrids[winCondition].SetActive(true);

                OnSubGameStatusChange?.Invoke(subGameIndex, gameStatus);
                break;
            case GameStatus.Draw:
                BackGround.SetActive(true);

                OnSubGameStatusChange?.Invoke(subGameIndex, gameStatus);
                break;
            case GameStatus.InPlay:
                break;
        }

        return true;

    }
}
