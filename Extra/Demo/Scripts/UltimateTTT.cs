using FishNet.Object;
using TMPro;
using UnityEngine;

public enum SlotOption
{
    None,
    X,
    O
}

public enum GameStatus
{
    InPlay,
    X,
    O,
    Draw
}

public class UltimateTTT : NetworkBehaviour
{
    public static SlotOption playerType = SlotOption.O;
    public static int currentGridPlayIndex = -1;

    public TMP_Text YouAreText;
    public TMP_Text WhoseTurn;

    public static GameStatus status = GameStatus.InPlay;

    public UltimateTTT_SubGame[] subGames = new UltimateTTT_SubGame[9];

    public GameObject[] winGrids = new GameObject[8];
    public GameObject[] currentGridVisuals = new GameObject[0];

    public void Start()
    {
        for (int i = 0; i < subGames.Length; i++)
        {
            subGames[i].OnSubGameSlotChange += SubGameSlotChange;
            subGames[i].OnSubGameStatusChange += SubGameStatusChange;
        }
    }


    public override void OnStartClient()
    {


        if (IsHostStarted)
        {
            Debug.Log("We are the host!");
            playerType = SlotOption.O;
            YouAreText.text = "You are [O]";

        }
        else if (IsClientOnlyStarted)
        {
            Debug.Log("We are the local client!");
            playerType = SlotOption.X;
            YouAreText.text = "You are [X]";
        }

    }



    public GameStatus[] subGameStatuses = new GameStatus[9]
    {
        GameStatus.InPlay,
        GameStatus.InPlay,
        GameStatus.InPlay,
        GameStatus.InPlay,
        GameStatus.InPlay,
        GameStatus.InPlay,
        GameStatus.InPlay,
        GameStatus.InPlay,
        GameStatus.InPlay
    };

    public static void temporaryPlayerChange()
    {
        if (playerType == SlotOption.O)
        {
            playerType = SlotOption.X;
        }
        else
        {
            playerType = SlotOption.O;
        }
        Debug.Log($"The player is now {playerType}");
    }

    public void SubGameSlotChange(int index, int slot, SlotOption slotOption)
    {


        Debug.Log($"Slot {slot} in grid {index} was changed to {slotOption}");

        if (subGameStatuses[slot] != GameStatus.InPlay)
        {
            //a slot that is not in play! give back control to the player
            if(UltimateTTT.currentGridPlayIndex != -1)
            {
                currentGridVisuals[UltimateTTT.currentGridPlayIndex].SetActive(false);
            }
            UltimateTTT.currentGridPlayIndex = -1;
        }
        else
        {
            if (UltimateTTT.currentGridPlayIndex != -1)
            {
                currentGridVisuals[UltimateTTT.currentGridPlayIndex].SetActive(false);
            }
            UltimateTTT.currentGridPlayIndex = slot;
            currentGridVisuals[slot].SetActive(true);

        }


        WhoseTurn.text = $"It is [{(UltimateTTT_NetworkManager._playerTurn == SlotOption.X ? "O" : "X")}] turn";

    }

    public void SubGameStatusChange(int index, GameStatus status)
    {
        subGameStatuses[index] = status;
        Debug.Log($"Grid {index} has finished with the status {status}");


        (GameStatus gameStatus, int winCondition) = UltimateTTT_WinConditions.CheckGameStatus(subGameStatuses);

        switch (gameStatus)
        {
            case GameStatus.X:
                Debug.Log("X Won!");
                UltimateTTT.status = gameStatus;
                winGrids[winCondition].SetActive(true);
                WhoseTurn.text = $"X has won!";

                break;
            case GameStatus.O:
                Debug.Log("O Won!");
                UltimateTTT.status = gameStatus;

                winGrids[winCondition].SetActive(true);
                WhoseTurn.text = $"O has won!";

                break;
            case GameStatus.Draw:
                Debug.Log("No Winner - Draw!");
                UltimateTTT.status = gameStatus;

                winGrids[winCondition].SetActive(true);
                WhoseTurn.text = $"Game is a Draw!";

                break;
            case GameStatus.InPlay:
                Debug.Log("There are still grids in play");
                break;
        }

        

        if (UltimateTTT.currentGridPlayIndex == index)
        {

            currentGridVisuals[UltimateTTT.currentGridPlayIndex].SetActive(false);

            UltimateTTT.currentGridPlayIndex = -1;
        }
    }

}



public static class UltimateTTT_WinConditions
{
    static int[][] winConditions = new int[][]
    {
            new int[] {0, 1, 2},
            new int[] {3, 4, 5},
            new int[] {6, 7, 8},
            new int[] {0, 3, 6},
            new int[] {1, 4, 7},
            new int[] {2, 5, 8},
            new int[] {0, 4, 8},
            new int[] {2, 4, 6}
    };


    public static (GameStatus, int) CheckSubGameStatus(SlotOption[] slots)
    {
        for (int i = 0; i < winConditions.Length; i++)
        {
            int[] winCondition = winConditions[i];
            if (slots[winCondition[0]] != SlotOption.None &&
                slots[winCondition[0]] == slots[winCondition[1]] &&
                slots[winCondition[1]] == slots[winCondition[2]])
            {
                if (slots[winCondition[0]] == SlotOption.X)
                {
                    return (GameStatus.X, i);
                }
                else if (slots[winCondition[0]] == SlotOption.O)
                {
                    return (GameStatus.O, i);
                }
            }
        }

        if (IsSubGameDraw(slots))
        {
            return (GameStatus.Draw, -1);
        }
        else
        {
            return (GameStatus.InPlay, -1);
        }
    }


    private static bool IsSubGameDraw(SlotOption[] slots)
    {
        foreach(SlotOption slot in slots)
        {
            if(slot == SlotOption.None)
            {
                //there is an empty slot - can't be a draw
                return false;
            }
        }

        return true;
    }



    public static (GameStatus, int) CheckGameStatus(GameStatus[] subGameStatuses)
    {
        for (int i = 0; i < winConditions.Length; i++)
        {
            int[] winCondition = winConditions[i];
            if (subGameStatuses[winCondition[0]] != GameStatus.InPlay &&
                        subGameStatuses[winCondition[0]] != GameStatus.Draw &&
                        subGameStatuses[winCondition[0]] == subGameStatuses[winCondition[1]] &&
                        subGameStatuses[winCondition[1]] == subGameStatuses[winCondition[2]])
            {
                return (subGameStatuses[winCondition[0]], i);
            }
        }

        if (IsGameDraw(subGameStatuses))
        {
            return (GameStatus.Draw, -1);
        }
        else
        {
            return (GameStatus.InPlay, -1);
        }
    }

    
    private static bool IsGameDraw(GameStatus[] subGameStatuses)
    {
        foreach (GameStatus status in subGameStatuses)
        {
            if (status == GameStatus.InPlay)
            {
                return false;
            }
        }

        return true;
    }

}