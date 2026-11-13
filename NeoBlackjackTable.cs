using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.Udon;

// VRChat UdonSharp: human dealer controls all actions. No AI opponent, wagers, or cash prizes.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class NeoBlackjackTable : UdonSharpBehaviour
{
    public const int MaxSeats = 10;
    [Header("UI")]
    public TMP_Text statusText;
    public TMP_Text resultsText;
    public TMP_Text cardsText;
    public TMP_Text dealerText;
    public TMP_Text selectedSeatText;
    public TMP_Text winnersText;
    [Header("3D card readouts - 10 player spots + dealer")]
    public TMP_Text[] tableCardTexts = new TMP_Text[10];
    public TMP_Text physicalDealerCards;
    public TMP_Text[] chipCounters = new TMP_Text[10];
    [Header("Settings")]
    public int handsPerSession = 3;
    public bool requireMasterForDealer = true;

    [UdonSynced] private string[] names = new string[10];
    [UdonSynced] private int[] playerIds = new int[10];
    [UdonSynced] private int[] wins = new int[10];
    [UdonSynced] private int[] losses = new int[10];
    [UdonSynced] private int[] pushes = new int[10];
    [UdonSynced] private int[] usedHands = new int[10];
    [UdonSynced] private string[] handLabels = new string[10];
    [UdonSynced] private int[] handTotals = new int[10];
    [UdonSynced] private int[] handAces = new int[10];
    [UdonSynced] private int[] cardCounts = new int[10];
    [UdonSynced] private bool[] stood = new bool[10];
    [UdonSynced] private int dealerId;
    [UdonSynced] private string dealerName = "Unassigned";
    [UdonSynced] private int dealerTotal;
    [UdonSynced] private int dealerAces;
    [UdonSynced] private int dealerCardCount;
    [UdonSynced] private string dealerCards = "";
    [UdonSynced] private bool handActive;
    [UdonSynced] private int selectedSeat;
    [UdonSynced] private bool dealerFinished;
    [UdonSynced] private int phase; // 0 waiting, 1 dealing, 2 resolving
    [UdonSynced] private int deckIndex;
    [UdonSynced] private int[] deck = new int[52];
    [UdonSynced] private string lastMessage = "Dealer: claim the desk to begin.";

    private void Start() { EnsureArrays(); Refresh(); }
    private void EnsureArrays()
    {
        if (names == null || names.Length != 10) names = new string[10];
        if (playerIds == null || playerIds.Length != 10) playerIds = new int[10];
        if (wins == null || wins.Length != 10) wins = new int[10];
        if (losses == null || losses.Length != 10) losses = new int[10];
        if (pushes == null || pushes.Length != 10) pushes = new int[10];
        if (usedHands == null || usedHands.Length != 10) usedHands = new int[10];
        if (handLabels == null || handLabels.Length != 10) handLabels = new string[10];
        if (handTotals == null || handTotals.Length != 10) handTotals = new int[10];
        if (handAces == null || handAces.Length != 10) handAces = new int[10];
        if (cardCounts == null || cardCounts.Length != 10) cardCounts = new int[10];
        if (stood == null || stood.Length != 10) stood = new bool[10];
        if (deck == null || deck.Length != 52) deck = new int[52];
    }
    public override void OnDeserialization() { Refresh(); }
    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (player.playerId == dealerId) { dealerId = 0; dealerName = "Unassigned"; lastMessage = "Dealer disconnected; desk available."; Save(); }
    }
    private bool CanDeal()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        return Utilities.IsValid(local) && local.playerId == dealerId;
    }
    private void Save() { RequestSerialization(); Refresh(); }
    private bool Own()
    {
        if (!Utilities.IsValid(Networking.LocalPlayer)) return false;
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        return Networking.IsOwner(gameObject);
    }
    public void ClaimDealer()
    {
        VRCPlayerApi p = Networking.LocalPlayer;
        if (!Utilities.IsValid(p)) return;
        if (requireMasterForDealer && !p.isMaster && !p.isInstanceOwner) { LocalMessage("Only instance master/owner may claim the desk."); return; }
        if (dealerId != 0 && dealerId != p.playerId) { LocalMessage("Desk already claimed by " + dealerName); return; }
        if (!Own()) return;
        dealerId = p.playerId; dealerName = p.displayName; lastMessage = "Dealer: " + dealerName; Save();
    }
    public void ReleaseDealer()
    {
        if (!CanDeal() || !Own()) return;
        dealerId = 0; dealerName = "Unassigned"; lastMessage = "Dealer desk released."; Save();
    }
    public void JoinSeat()
    {
        VRCPlayerApi p = Networking.LocalPlayer;
        if (!Utilities.IsValid(p) || handActive) { LocalMessage("Wait for the current hand to finish."); return; }
        for (int i = 0; i < 10; i++) if (playerIds[i] == p.playerId) { LocalMessage("Already seated at " + (i+1)); return; }
        int seat = -1;
        for (int i = 0; i < 10; i++) if (playerIds[i] == 0) { seat = i; break; }
        if (seat < 0) { LocalMessage("All 10 seats are occupied."); return; }
        if (!Own()) return;
        playerIds[seat] = p.playerId; names[seat] = p.displayName;
        wins[seat] = 0; losses[seat] = 0; pushes[seat] = 0; usedHands[seat] = 0;
        lastMessage = p.displayName + " joined seat " + (seat + 1); Save();
    }
    public void LeaveSeat()
    {
        if (handActive) { LocalMessage("Cannot leave during an active hand."); return; }
        VRCPlayerApi p = Networking.LocalPlayer;
        if (!Utilities.IsValid(p)) return;
        for (int i = 0; i < 10; i++) if (playerIds[i] == p.playerId)
        {
            if (!Own()) return;
            playerIds[i] = 0; names[i] = ""; handLabels[i] = ""; usedHands[i] = 0;
            lastMessage = p.displayName + " left seat " + (i + 1); Save(); return;
        }
    }
    public void SelectNextSeat()
    {
        if (!CanDeal() || !Own()) return;
        selectedSeat = (selectedSeat + 1) % 10; Save();
    }
    public void SelectPreviousSeat()
    {
        if (!CanDeal() || !Own()) return;
        selectedSeat = (selectedSeat + 9) % 10; Save();
    }
    private void Shuffle()
    {
        for (int i = 0; i < 52; i++) deck[i] = i;
        for (int i = 51; i > 0; i--)
        {
            int j = Random.Range(0, i + 1); int tmp = deck[i]; deck[i] = deck[j]; deck[j] = tmp;
        }
        deckIndex = 0;
    }
    private string CardName(int card)
    {
        int rank = card % 13; int suit = card / 13;
        string face = rank == 0 ? "A" : rank == 10 ? "J" : rank == 11 ? "Q" : rank == 12 ? "K" : "" + (rank + 1);
        string symbol = suit == 0 ? "S" : suit == 1 ? "H" : suit == 2 ? "D" : "C";
        return face + symbol;
    }
    private int CardValue(int card) { int r = card % 13; return r == 0 ? 11 : r >= 10 ? 10 : r + 1; }
    private int AdjustTotal(int total, int aces)
    {
        while (total > 21 && aces > 0) { total -= 10; aces--; }
        return total;
    }
    private void GiveCard(int seat)
    {
        if (deckIndex >= 52) Shuffle();
        int c = deck[deckIndex++];
        if (seat < 0)
        {
            dealerTotal += CardValue(c); if (c % 13 == 0) dealerAces++;
            dealerCardCount++; dealerCards += CardName(c) + " ";
        }
        else
        {
            handTotals[seat] += CardValue(c); if (c % 13 == 0) handAces[seat]++;
            cardCounts[seat]++; handLabels[seat] += CardName(c) + " ";
        }
    }
    private int Value(int seat) { return AdjustTotal(handTotals[seat], handAces[seat]); }
    public void StartHand()
    {
        if (!CanDeal() || !Own() || handActive) return;
        int eligible = 0;
        for (int i = 0; i < 10; i++) if (playerIds[i] != 0 && usedHands[i] < handsPerSession) eligible++;
        if (eligible == 0) { LocalMessage("No eligible players. Reset session if all chips used."); return; }
        Shuffle(); handActive = true; phase = 1; dealerFinished = false;
        dealerTotal = 0; dealerAces = 0; dealerCardCount = 0; dealerCards = "";
        for (int i = 0; i < 10; i++)
        {
            handTotals[i] = 0; handAces[i] = 0; cardCounts[i] = 0; stood[i] = false; handLabels[i] = "";
            if (playerIds[i] != 0 && usedHands[i] < handsPerSession) { GiveCard(i); GiveCard(i); }
        }
        GiveCard(-1); GiveCard(-1);
        lastMessage = "Hand started. Dealer directs players to hit/stand."; Save();
    }
    public void HitSelected()
    {
        if (!CanDeal() || !Own() || !handActive || phase != 1) return;
        int s = selectedSeat;
        if (playerIds[s] == 0 || usedHands[s] >= handsPerSession || stood[s] || cardCounts[s] == 0) return;
        GiveCard(s); if (Value(s) >= 21) stood[s] = true;
        lastMessage = "Seat " + (s+1) + " hit: " + Value(s); Save();
    }
    public void StandSelected()
    {
        if (!CanDeal() || !Own() || !handActive || phase != 1) return;
        int s = selectedSeat;
        if (playerIds[s] == 0 || cardCounts[s] == 0) return;
        stood[s] = true; lastMessage = "Seat " + (s+1) + " stands."; Save();
    }
    public void BeginDealerTurn()
    {
        if (!CanDeal() || !Own() || !handActive || phase != 1) return;
        for (int i = 0; i < 10; i++)
            if (cardCounts[i] > 0 && !stood[i] && Value(i) < 21)
            { LocalMessage("Finish all player turns before revealing the dealer."); return; }
        phase = 2; lastMessage = "Dealer turn started."; Save();
    }
    public void DealerHit()
    {
        if (!CanDeal() || !Own() || !handActive || phase != 2) return;
        GiveCard(-1); if (AdjustTotal(dealerTotal, dealerAces) >= 21) dealerFinished = true; lastMessage = "Dealer drew a card."; Save();
    }
    public void DealerStand() { if (!CanDeal() || !Own() || !handActive || phase != 2) return; dealerFinished = true; lastMessage = "Dealer stands at " + AdjustTotal(dealerTotal, dealerAces); Save(); }
    public void ResolveHand()
    {
        if (!CanDeal() || !Own() || !handActive || phase != 2 || !dealerFinished) return;
        int dv = AdjustTotal(dealerTotal, dealerAces);
        bool dealerBlackjack = dealerCardCount == 2 && dv == 21;
        for (int i = 0; i < 10; i++)
        {
            if (playerIds[i] == 0 || cardCounts[i] == 0) continue;
            int pv = Value(i); bool playerBlackjack = cardCounts[i] == 2 && pv == 21;
            if (pv > 21) losses[i]++;
            else if (playerBlackjack && !dealerBlackjack) wins[i]++;
            else if (dealerBlackjack && !playerBlackjack) losses[i]++;
            else if (playerBlackjack && dealerBlackjack) pushes[i]++;
            else if (dv > 21 || pv > dv) wins[i]++;
            else if (pv < dv) losses[i]++;
            else pushes[i]++;
            usedHands[i]++;
        }
        handActive = false; phase = 0; lastMessage = "Hand resolved. Chips consumed: one per participating player."; Save();
    }
    public void ResetSession()
    {
        if (!CanDeal() || !Own() || handActive) return;
        for (int i = 0; i < 10; i++) { wins[i] = 0; losses[i] = 0; pushes[i] = 0; usedHands[i] = 0; handLabels[i] = ""; }
        dealerCards = ""; dealerTotal = 0; dealerCardCount = 0;
        lastMessage = "New three-hand session. Scores cleared."; Save();
    }
    private void LocalMessage(string s) { if (statusText != null) statusText.text = s; }
    private void Refresh()
    {
        EnsureArrays();
        if (statusText != null) statusText.text = lastMessage;
        if (dealerText != null) dealerText.text = "DEALER: " + dealerName + "\nCards: " + dealerCards + " Total: " + AdjustTotal(dealerTotal, dealerAces);
        if (selectedSeatText != null) selectedSeatText.text = "SELECTED SEAT: " + (selectedSeat + 1) + (handActive ? " | LIVE" : " | READY");
        string board = "SEAT  PLAYER                 W  L  P  CHIPS\n";
        string cards = "PLAYER HANDS\n";
        for (int i = 0; i < 10; i++)
        {
            if (playerIds[i] == 0) { board += (i+1) + "     —\n"; continue; }
            string n = names[i] == null ? "Player" : names[i];
            if (n.Length > 18) n = n.Substring(0,18);
            board += (i+1) + "  " + n + "  " + wins[i] + "  " + losses[i] + "  " + pushes[i] + "  " + Mathf.Max(0,handsPerSession-usedHands[i]) + "\n";
            cards += (i+1) + ": " + handLabels[i] + " [" + Value(i) + "]" + (stood[i] ? " STAND" : "") + "\n";
        }
        if (winnersText != null)
        {
            string summary = "RESULTS / TOP SCORES\n";
            int best = -1; int second = -1; int third = -1;
            for (int i = 0; i < 10; i++)
            {
                if (playerIds[i] == 0) continue;
                if (best < 0 || wins[i] > wins[best]) { third = second; second = best; best = i; }
                else if (second < 0 || wins[i] > wins[second]) { third = second; second = i; }
                else if (third < 0 || wins[i] > wins[third]) third = i;
            }
            if (best >= 0) summary += "1. " + names[best] + "  " + wins[best] + " W\n";
            if (second >= 0) summary += "2. " + names[second] + "  " + wins[second] + " W\n";
            if (third >= 0) summary += "3. " + names[third] + "  " + wins[third] + " W\n";
            summary += "Ties require scorekeeper review.";
            winnersText.text = summary;
        }
        if (physicalDealerCards != null) physicalDealerCards.text = "DEALER\n" + dealerCards + "\n" + AdjustTotal(dealerTotal, dealerAces);
        if (tableCardTexts != null) for (int i = 0; i < 10 && i < tableCardTexts.Length; i++)
            if (tableCardTexts[i] != null) tableCardTexts[i].text = playerIds[i] == 0 ? "SEAT " + (i + 1) + "\nOPEN" : names[i] + "\n" + handLabels[i] + "\n" + (cardCounts[i] > 0 ? "TOTAL " + Value(i) : "READY");
        if (chipCounters != null) for (int i = 0; i < 10 && i < chipCounters.Length; i++)
            if (chipCounters[i] != null) chipCounters[i].text = playerIds[i] == 0 ? "" : "HANDS LEFT " + Mathf.Max(0, handsPerSession - usedHands[i]);
        if (resultsText != null) resultsText.text = board;
        if (cardsText != null) cardsText.text = cards;
    }
}
