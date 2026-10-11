using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

/// <summary>
/// Shows SourceMapManager's state: the lobby board (map slots with thumbnail, name, author, votes) or a small panel
/// inside a map (time left, rtv count). Every field is optional, so one script serves both.
/// Buttons are SourceMapButtons in it. Without a Manager it finds the object named "SourceMapManager" (prefab use).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapScreen : UdonSharpBehaviour
{
    public SourceMapManager manager;
    public TextMeshProUGUI status;
    [Header("Map slots (lobby board)")]
    public GameObject[] slotRoots;
    public RawImage[] slotImages;
    public TextMeshProUGUI[] slotTexts;
    [Tooltip("Shown on the slot you voted for")]
    public Image[] slotSelected;
    [Tooltip("\"Extend current map\" slot, shown after a time limit")]
    public GameObject extendRoot;
    public TextMeshProUGUI extendText;
    [Header("Shown only to the instance owner / master")]
    public GameObject adminRoot;
    public TextMeshProUGUI adminText;

    private void Start()
    {
        // Dragged in from the prefab: find the lobby's manager by name and register, so it gets refreshed.
        if (manager == null)
        {
            GameObject found = GameObject.Find("SourceMapManager");
            if (found != null) manager = found.GetComponent<SourceMapManager>();
        }
        if (manager != null)
        {
            manager._AddScreen(this);
            Refresh();
        }
    }

    public void Refresh()
    {
        if (manager == null) return;
        VRCPlayerApi local = Networking.LocalPlayer;
        bool lobby = manager.state == SourceMapManager.StateLobby;
        int[] candidates = manager.candidates;
        int left = manager.SecondsLeft();

        if (status != null)
        {
            string s;
            if (lobby)
            {
                s = manager.extendOffered ? "Time is up! Vote for the next map" : "Vote for the next map";
                if (manager.locked) s += "\n<size=70%><color=#ffb547>Vote locked by the instance owner</color></size>";
                else if (left >= 0) s += "\n<size=70%><color=#8b93a3>" + Clock(left) + "</color></size>";
                else s += "\n<size=70%><color=#8b93a3>The timer starts with the first vote</color></size>";
            }
            else
            {
                s = "Now playing: " + manager.MapName(manager.currentMap);
                s += "\n<size=75%><color=#8b93a3>" + (left >= 0 ? Clock(left) + " left" : "No time limit");
                s += "   |   RTV " + manager.rtvCount + "/" + manager.RtvNeeded() + (manager.localRtv ? " (you)" : "") + "</color></size>";
                if (manager.localMap != manager.currentMap) s += "\n<size=75%><color=#4c8dff>Press Rejoin to play</color></size>";
            }
            status.text = s;
        }

        if (slotRoots != null)
            for (int i = 0; i < slotRoots.Length; i++)
            {
                bool shown = i < candidates.Length;
                if (slotRoots[i] != null) slotRoots[i].SetActive(shown);
                if (!shown) continue;
                SourceMapInfo info = manager.maps[candidates[i]];
                if (slotImages != null && i < slotImages.Length && slotImages[i] != null)
                    slotImages[i].texture = info != null ? info.thumbnail : null;
                if (slotTexts != null && i < slotTexts.Length && slotTexts[i] != null)
                {
                    string t = info != null ? "<b>" + info.mapName + "</b>" + (info.author != "" ? "\n<size=75%><color=#8b93a3>by " + info.author + "</color></size>" : "") : "?";
                    if (lobby && i < manager.votes.Length) t += "\n<color=#8b93a3>" + manager.votes[i] + (manager.votes[i] == 1 ? " vote" : " votes") + "</color>";
                    if (manager.localVote == i) t += " <color=#4c8dff>(you)</color>";
                    slotTexts[i].text = t;
                }
                if (slotSelected != null && i < slotSelected.Length && slotSelected[i] != null)
                    slotSelected[i].enabled = lobby && manager.localVote == i;
            }

        bool extend = lobby && manager.extendOffered;
        if (extendRoot != null) extendRoot.SetActive(extend);
        if (extend && extendText != null)
        {
            int n = manager.votes.Length > 0 ? manager.votes[manager.votes.Length - 1] : 0;
            extendText.text = "Extend " + manager.MapName(manager.currentMap) + "\n" + n + (n == 1 ? " vote" : " votes")
                + (manager.localVote == candidates.Length ? " <color=#ffffff>(you)</color>" : "");
        }

        bool admin = manager.IsAdmin(local);
        if (adminRoot != null) adminRoot.SetActive(admin);
        if (admin && adminText != null)
            adminText.text = manager.forceArmed ? "<color=#ff6b6b>Press a map to force it</color>" : "Owner controls";
    }

    private string Clock(int seconds)
    {
        int m = seconds / 60, s = seconds % 60;
        return m + ":" + (s < 10 ? "0" : "") + s;
    }
}
