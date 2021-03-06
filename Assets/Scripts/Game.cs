﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Game : MonoBehaviour
{
    #region Unity Bindings

    public Player[] players;
    public GameObject gameMap;
    public Player currentPlayer;
    public GameObject gameSavedPopup;
    public GameObject minigameResultPopup;
    public Text minigameStatusText;
    public Text minigameScoreText;
    public Text minigameRewardText;
    public GameObject minigameLoading;
    public GameObject endGamePopup;
    public Text endGameWinnerText;

    #endregion

    #region Private Fields

    /// <summary>
    /// The value to scale down the minigame score by.
    /// Currently, the maximum minigame score is 170 (which you get if you win
    /// with 100% health left), and the modifier is set so that only if you get
    /// this maximum score, will you be given 6 points.
    /// </summary>
    public const float minigameScoreScaleDownModifier = 28f + (1f / 3f);
    const string minigameStatusTextFormat = "You {0} the minigame!";
    const string minigameScoreTextFormat = "You scored {0} points, which gives you";
    const string minigameRewardTextFormat = "{0} beer and {1} knowledge";

    TurnState turnState;
    bool gameFinished = false;
    bool testMode = false;
    Player pvcDiscoveredByLast;

    #endregion

    #region Public Properties

    /// <summary>
    /// Used to set up the player configuration from the main menu.
    /// </summary>
    public static List<int> HumanPlayersCount { get; set; }
    /// <summary>
    /// The current turn state.
    /// </summary>
    public TurnState TurnState
    {
        get { return turnState; }
        set { turnState = value; }
    }
    /// <summary>
    /// The last player to discover the PVC.
    /// </summary>
    public Player PvcDiscoveredByLast
    {
        get { return pvcDiscoveredByLast; }
        set { pvcDiscoveredByLast = value; }
    }
    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public bool IsFinished { get { return gameFinished; } }
    /// <summary>
    /// Whether test mode is enabled.
    /// If so, then Update() stop processing.
    /// </summary>
    public bool TestModeEnabled
    {
        get { return testMode; }
        set { testMode = value; }
    }
    /// <summary>
    /// Stores the memento of the game to restore.
    /// If this is not null when Initializer.Awake fires, then the stored memento is
    /// restored.
    /// </summary>
    public static SerializableGame GameToRestore { get; set; }

    #endregion

    #region Initialization

    /// <summary>
    /// Initialize the game.
    /// </summary>
    public void Initialize()
    {
        HumanPlayersCount = HumanPlayersCount ?? new List<int> { 0, 0, 0, 0 };

        // create a specified number of human players
        CreatePlayers(HumanPlayersCount);

        // initialize the map and allocate players to landmarks
        InitializeMap();

        // initialize the turn state
        turnState = TurnState.Move1;

        // set first human player as the current player
        currentPlayer = players[0];
        currentPlayer = players[HumanPlayersCount.FindIndex(players => players == 0)];
        currentPlayer.Gui.Activate();
        players[HumanPlayersCount.FindIndex(players => players == 0)].IsActive = true;

        // update GUIs
        UpdateGUI();
    }

    /// <summary>
    /// Initialize all sectors, allocate players to landmarks,
    /// and spawn units and PVC.
    /// </summary>
    public void InitializeMap()
    {
        // get an array of all sectors
        Sector[] sectors = gameMap.GetComponentsInChildren<Sector>();

        // initialize each sector
        foreach (Sector sector in sectors)
        {
            sector.Initialize();
        }

        // get an array of all sectors containing landmarks
        Sector[] landmarkedSectors = GetLandmarkedSectors(sectors);

        // ensure there are at least as many landmarks as players
        if (landmarkedSectors.Length < players.Length)
        {
            throw new Exception("Must have at least as many landmarks as players; only " + landmarkedSectors.Length.ToString() + " landmarks found for " + players.Length.ToString() + " players.");
        }

        // randomly allocate sectors to players
        foreach (Player player in players)
        {
            bool playerAllocated = false;
            while (!playerAllocated)
            {

                // choose a landmarked sector at random
                int randomIndex = UnityEngine.Random.Range(0, landmarkedSectors.Length);

                // if the sector is not yet allocated, allocate the player
                if (landmarkedSectors[randomIndex].Owner == null)
                {
                    player.Capture(landmarkedSectors[randomIndex]);
                    playerAllocated = true;
                }

                // retry until player is allocated
            }
        }

        // spawn units for each player
        foreach (Player player in players)
        {
            player.SpawnUnits();
        }

        //sawn the PVC
        if (PvcDiscoveredByLast == null)
        {
            SpawnPVC();
            PvcDiscoveredByLast = null;
        }
    }

    /// <summary>
    /// Initialises the players using the specified number of human players.
    /// </summary>
    /// <param name="listOfPlayers">List of all players assigned at MainMenu.</param>
    public void CreatePlayers(List<int> listOfPlayers)
    {
        // mark specified players as human
        for (int i = 0; i < listOfPlayers.Count; i++)
        {
            if (listOfPlayers[i] == 0)
            {
                players[i].IsHuman = true;
            }
        }

        // give all players a reference to this game
        // and initialize their GUIs
        for (int i = 0; i < 4; i++)
        {
            players[i].Id = i;
            players[i].Game = this;
            players[i].Gui.Initialize(players[i], i + 1);
        }
    }

    #endregion

    #region Serialization

    /// <summary>
    /// Saves the game state to a memento (stored as <see cref="SerializableGame"/>).
    /// This is the top level memento handler, meaning that the entire game state is
    /// stored in the result, and can be completely restored by the result.
    /// </summary>
    /// <returns>The memento of the current game state.</returns>
    public SerializableGame SaveToMemento()
    {
        SerializablePlayer[] sPlayers = new SerializablePlayer[players.Length];
        for (int i = 0; i < players.Length; i++)
            sPlayers[i] = players[i].SaveToMemento();
        Sector[] sectors = gameMap.GetComponentsInChildren<Sector>();
        SerializableSector[] sSectors = new SerializableSector[sectors.Length];
        for (int i = 0; i < sectors.Length; i++)
            sSectors[i] = sectors[i].SaveToMemento();
        return new SerializableGame
        {
            turnState = turnState,
            players = sPlayers,
            sectors = sSectors,
            currentPlayerId = currentPlayer.Id,
            lastDiscovererOfPvcId = PvcDiscoveredByLast?.Id

        };
    }

    /// <summary>
    /// Restores the game state from a memento. This is the top level memento handler,
    /// meaning that the entire game state can be restored from the given memento.
    /// </summary>
    /// <param name="memento">The memento to restore.</param>
    public void RestoreFromMemento(SerializableGame memento)
    {
        turnState = memento.turnState;
        Sector[] sectors = gameMap.GetComponentsInChildren<Sector>();
        for (int i = 0; i < memento.sectors.Length; i++)
            sectors[i].RestoreFromMemento(memento.sectors[i], players);
        for (int i = 0; i < memento.players.Length; i++)
        {
            players[i].RestoreFromMemento(memento.players[i]);
            players[i].Game = this;
            players[i].Gui.Initialize(players[i], i + 1);
        }
        currentPlayer = players[memento.currentPlayerId];
        currentPlayer.Gui.Activate();
        if (memento.lastDiscovererOfPvcId.HasValue)
            PvcDiscoveredByLast = players[memento.lastDiscovererOfPvcId.Value];
        MinigameFinishedProcess();
        UpdateGUI();
    }

    #endregion

    #region MonoBehaviour

    void Update()
    {
        if (testMode)
            return;
        UpdateMain();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Randomly spawn the PVC
    /// </summary>
    public void SpawnPVC()
    {
        Sector[] sectors = gameMap.GetComponentsInChildren<Sector>();

        Sector lastPvcSector = sectors.Where(s => s.HasPVC).FirstOrDefault();
        Sector[] availableSectors = sectors.Where(s => s.AllowPVC && s != lastPvcSector).ToArray();
        Sector randomSector = availableSectors[UnityEngine.Random.Range(0, availableSectors.Length)];

        if (lastPvcSector == null)
        {
            randomSector.HasPVC = true;
            Debug.Log("Allocated PVC initially at " + randomSector);
        }
        else
        {
            randomSector.HasPVC = true;
            lastPvcSector.HasPVC = false;
            if (PvcDiscoveredByLast != null)
                Debug.Log("Previous Player that found it is " + PvcDiscoveredByLast);
            Debug.Log("Allocated PVC to a new location, which is at " + randomSector);
            Debug.Log("Player that found it is " + currentPlayer);
            Debug.Log("Last Player that found it is " + PvcDiscoveredByLast);
        }
    }

    /// <summary>
    /// Return a list of all sectors that contain landmarks from the given array.
    /// </summary>
    /// <returns>The landmarked sectors.</returns>
    /// <param name="sectors">Sectors.</param>
    Sector[] GetLandmarkedSectors(Sector[] sectors)
    {
        List<Sector> landmarkedSectors = new List<Sector>();
        foreach (Sector sector in sectors)
            if (sector.Landmark != null)
                landmarkedSectors.Add(sector);

        return landmarkedSectors.ToArray();
    }

    /// <summary>
    /// Return true if no unit is selected, false otherwise.
    /// </summary>
    /// <returns>Return true if no unit is selected, false otherwise.</returns>
    public bool NoUnitSelected()
    {
        foreach (Player player in players) // scan through each player
            foreach (Unit unit in player.units) // scan through each unit of each player
                if (unit.IsSelected == true) // if a selected unit is found, return false
                    return false;

        // otherwise, return true
        return true;
    }

    /// <summary>
    /// Set the current player to the next player in the order.
    /// </summary>
    public void NextPlayer()
    {
        // deactivate the current player
        currentPlayer.IsActive = false;
        currentPlayer.Gui.Deactivate();

        // find the index of the current player
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == currentPlayer)
            {
                // set the next player's index
                int nextPlayerIndex = i + 1;

                // if end of player list is reached, loop back to the first player
                if (nextPlayerIndex == players.Length)
                {
                    currentPlayer = players[0];
                    players[0].IsActive = true;
                    players[0].Gui.Activate();
                }

                // otherwise, set the next player as the current player
                else
                {
                    currentPlayer = players[nextPlayerIndex];
                    players[nextPlayerIndex].IsActive = true;
                    players[nextPlayerIndex].Gui.Activate();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Change the turn state to the next in the order,
    /// or to initial turn state if turn is completed.
    /// </summary>
    public void NextTurnState()
    {
        switch (turnState)
        {
            case TurnState.Move1:
                turnState = TurnState.Move2;
                break;

            case TurnState.Move2:
                turnState = TurnState.EndOfTurn;
                break;

            case TurnState.EndOfTurn:
                turnState = TurnState.Move1;
                break;
        }

        UpdateGUI();
    }

    /// <summary>
    /// End the current turn.
    /// </summary>
    public void EndTurn()
    {
        turnState = TurnState.EndOfTurn;
    }

    /// <summary>
    /// Return the winning player, or null if no winner yet.
    /// </summary>
    /// <returns>Return the winning player, or null if no winner yet.</returns>
    public Player GetWinner()
    {
        Player winner = null;

        // scan through each player
        foreach (Player player in players)
        {
            // if the player hasn't been eliminated
            if (!player.IsEliminated)
            {
                // if this is the first player found that hasn't been eliminated,
                // assume the player is the winner
                if (winner == null)
                    winner = player;

                // if another player that was not eliminated was already,
                // found, then return null
                else
                    return null;
            }
        }

        // if only one player hasn't been eliminated, then return it as the winner
        return winner;
    }

    /// <summary>
    /// Ends the game.
    /// </summary>
    public void EndGame()
    {
        endGameWinnerText.text = "Player " + (GetWinner().Id + 1).ToString() + " is the winner!";
        endGamePopup.SetActive(true);

        gameFinished = true;
        currentPlayer.IsActive = false;
        currentPlayer = null;
        turnState = TurnState.NULL;
        MainMenu.ClearSave();
        Debug.Log("GAME FINISHED");
    }

    /// <summary>
    /// Update all players' GUIs.
    /// </summary>
    public void UpdateGUI()
    {
        for (int i = 0; i < 4; i++)
            players[i].Gui.UpdateDisplay();
    }

    /// <summary>
    /// The main body of each frame update.
    /// </summary>
    public void UpdateMain()
    {
        // at the end of each turn, check for a winner and end the game if
        // necessary; otherwise, start the next player's turn

        // if the current turn has ended
        if (turnState == TurnState.EndOfTurn)
        {

            // if there is no winner yet
            if (GetWinner() == null)
            {

                // start the next player's turn
                NextPlayer();
                NextTurnState();

                // skip eliminated players and non-human players
                while (currentPlayer.IsEliminated || !currentPlayer.IsHuman)
                    NextPlayer();

                // spawn units for the next player
                currentPlayer.SpawnUnits();
            }
            else if (!gameFinished)
                EndGame();
        }
    }

    /// <summary>
    /// Quits the current game.
    /// </summary>
    public void QuitGame()
    {
        SceneManager.LoadScene("MainMenuScene");
    }

    /// <summary>
    /// Save the current game state to a file for later loading
    /// </summary>
    public void SaveGame()
    {
        //Save game state to our static variable
        SerializableGame memento = SaveToMemento();
        // Open the file where we would serialize.
        FileStream fs = new FileStream(MainMenu.SaveGameDataPath, FileMode.OpenOrCreate);

        // Construct a BinaryFormatter and use it to serialize the data to the stream.
        BinaryFormatter formatter = new BinaryFormatter();
        try
        {
            formatter.Serialize(fs, memento);
            StartCoroutine(Helpers.ShowPopUpMessage(gameSavedPopup, 0.3f, 1, 0.3f, "Game saved"));
        }
        catch (SerializationException ex)
        {
            //Debug.Log("Failed to serialize. Reason: " + ex.Message);
            //throw ex;
            StartCoroutine(Helpers.ShowPopUpMessage(gameSavedPopup, 0.3f, 1, 0.3f,
                                                    (string.Format("Unable to save game ({0})", ex.Message))));
        }
        finally
        {
            fs.Close();
        }
    }

    /// <summary>
    /// Prepares the game for entering the minigame. This means respawning the PVC and saving the
    /// game state to allow restoring later. This function will also show a loading panel so
    /// the player knows what is going on.
    /// </summary>
    public void PrepareForMinigame()
    {
        PvcDiscoveredByLast = currentPlayer;
        SpawnPVC();
        GameToRestore = SaveToMemento();
        minigameLoading.SetActive(true);
    }

    /// <summary>
    /// Checks if a minigame has just finished, and if so, processes the result.
    /// </summary>
    public void MinigameFinishedProcess()
    {
        GameObject dataStore = GameObject.Find("DataStore");
        if (dataStore != null)
        {
            // if we get here, it means that a minigame just occurred

            // finalize data store to get result
            var result = dataStore.GetComponent<DataStore>().Finalize();
            Debug.Log($"minigame score: {result.Score}, success: {result.Succeeded}");

            // reward calculation
            // reward is identical ATM for beer and knowledge, but could be different
            int reward = Mathf.FloorToInt(result.Score / minigameScoreScaleDownModifier);

            // update result popup
            minigameStatusText.text = string.Format(minigameStatusTextFormat, result.Succeeded ? "beat" : "failed");
            minigameScoreText.text = string.Format(minigameScoreTextFormat, result.Score);
            minigameRewardText.text = string.Format(minigameRewardTextFormat, reward, reward);
            // show popup
            minigameResultPopup.SetActive(true);

            // apply rewards
            currentPlayer.Beer += reward; // add beer
            currentPlayer.Knowledge += reward; // add knowledge
        }
    }

    #endregion
}
