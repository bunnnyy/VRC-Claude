using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

/// <summary>
/// Shows SourceMapManager's state: the lobby board (map slots with thumbnail, name, author, votes) or a small panel
/// inside a map (time left, rtv count). Every field is optional, so one script serves both.
/// Buttons are SourceMapButtons next to it.
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
    [Tooltip("\"Extend current map\" slot, shown after a time limit")]
    public GameObject extendRoot;
    public TextMeshProUGUI extendText;
    [Header("Shown only to the instance owner / master")]
    public GameObject adminRoot;
    public TextMeshProUGUI adminText;

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
                if (manager.locked) s += "\n<color=#ffb040>Vote locked by the instance owner</color>";
                else if (left >= 0) s += "\n" + Clock(left);
                else s += "\nThe timer starts with the first vote";
            }
            else
            {
                s = "Now playing: " + manager.MapName(manager.currentMap);
                s += "\n" + (left >= 0 ? Clock(left) + " left" : "No time limit");
                s += "   RTV " + manager.rtvCount + "/" + manager.RtvNeeded() + (manager.localRtv ? " (you)" : "");
                if (manager.localMap != manager.currentMap) s += "\nPress Rejoin to play";
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
                    string t = info != null ? "<b>" + info.mapName + "</b>" + (info.author != "" ? "\n<size=60%>by " + info.author + "</size>" : "") : "?";
                    if (lobby && i < manager.votes.Length) t += "\n" + manager.votes[i] + (manager.votes[i] == 1 ? " vote" : " votes");
                    if (manager.localVote == i) t += " <color=#60ff60>(you)</color>";
                    slotTexts[i].text = t;
                }
            }

        bool extend = lobby && manager.extendOffered;
        if (extendRoot != null) extendRoot.SetActive(extend);
        if (extend && extendText != null)
        {
            int n = manager.votes.Length > 0 ? manager.votes[manager.votes.Length - 1] : 0;
            extendText.text = "Extend " + manager.MapName(manager.currentMap) + "\n" + n + (n == 1 ? " vote" : " votes")
                + (manager.localVote == candidates.Length ? " <color=#60ff60>(you)</color>" : "");
        }

        bool admin = manager.IsAdmin(local);
        if (adminRoot != null) adminRoot.SetActive(admin);
        if (admin && adminText != null)
            adminText.text = manager.forceArmed ? "<color=#ff6060>Press a map to force it</color>" : "Owner controls";
    }

    private string Clock(int seconds)
    {
        int m = seconds / 60, s = seconds % 60;
        return m + ":" + (s < 10 ? "0" : "") + s;
    }
}
