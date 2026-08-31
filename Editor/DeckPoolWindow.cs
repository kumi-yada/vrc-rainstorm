using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;
using org.kumagee;

namespace org.kumagee.EditorTools
{
    // Edit-time tooling for the card decks.
    //
    // VRCObjectPool.Pool has to be filled in at edit time: Udon cannot create
    // GameObjects, and the pool's ownership sync is built against a fixed array, so
    // there is no runtime shortcut. The trap is that every count in DeckManager
    // derives from Pool.Length - not from how many cards are parented under the deck
    // - so 104 cards in the hierarchy with 52 in the array silently deals a broken
    // game: the last column comes up short and the stock reads empty.
    //
    // Card identity is assigned from the pool index at runtime (DeckManager.Start),
    // so the cards are interchangeable and cloning one to fill out a deck is safe.
    // Nothing per-card has to be authored.
    public class DeckPoolWindow : EditorWindow
    {
        private DeckManager deck;
        private int targetCount = 104;
        private Vector2 scroll;
        private string report = "";

        [MenuItem("Tools/Solitaire/Deck Pool Builder")]
        private static void Open()
        {
            DeckPoolWindow window = GetWindow<DeckPoolWindow>(false, "Deck Pool", true);
            window.PickFromSelection();
            window.Refresh();
        }

        private void OnSelectionChange()
        {
            PickFromSelection();
            Refresh();
            Repaint();
        }

        private void PickFromSelection()
        {
            foreach (GameObject go in Selection.gameObjects)
            {
                DeckManager found = go.GetComponentInChildren<DeckManager>(true);
                if (found == null) found = go.GetComponentInParent<DeckManager>();
                if (found != null)
                {
                    deck = found;
                    return;
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Fills a deck's VRCObjectPool.Pool array from the card objects parented under the pool. " +
                "Pool.Length is what DeckManager counts cards by, so it has to match the hierarchy. " +
                "The pool may live on any GameObject; this reads the deck's Pool reference.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            deck = (DeckManager)EditorGUILayout.ObjectField("Deck", deck, typeof(DeckManager), true);
            if (EditorGUI.EndChangeCheck()) Refresh();

            if (deck == null)
            {
                EditorGUILayout.HelpBox("Select a deck (a GameObject with a DeckManager).", MessageType.Info);
                return;
            }

            targetCount = EditorGUILayout.IntField(
                new GUIContent("Target card count", "104 for Spider two-suit (8 copies of 13 ranks), 52 for a standard deck."),
                targetCount);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(deck == null))
            {
                if (GUILayout.Button("Rebuild Pool From Children"))
                {
                    RebuildPool(deck);
                    Refresh();
                }
                if (GUILayout.Button($"Clone Cards Up To {targetCount}, Then Rebuild"))
                {
                    CloneUpTo(deck, targetCount);
                    RebuildPool(deck);
                    Refresh();
                }
                if (GUILayout.Button("Validate Only")) Refresh();
            }

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.SelectableLabel(report, EditorStyles.textArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // The pool is an inspector reference on DeckManager and may live on a different
        // GameObject entirely, so it is resolved the same way the runtime does:
        // assigned reference first, same-object component as the legacy fallback.
        private static VRCObjectPool ResolvePool(DeckManager target)
        {
            if (target == null) return null;
            if (target.Pool != null) return target.Pool;
            return target.GetComponent<VRCObjectPool>();
        }

        // Pool entries are the card *roots* - the objects DeckManager reads
        // GetComponentInChildren<CardLogic>() off - so this collects direct children
        // that contain a CardLogic, not the CardLogic components themselves.
        //
        // They are collected from under the *pool*, not the deck. Those used to be the
        // same object; with a detached pool they are not, and the pool's transform is
        // what matters because that is where Solitaire parks cards (cardHome =
        // pool.transform) when it detaches them.
        private static List<GameObject> CollectCards(VRCObjectPool pool)
        {
            List<GameObject> cards = new List<GameObject>();
            if (pool == null) return cards;
            Transform t = pool.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child.GetComponentInChildren<CardLogic>(true) == null) continue;
                cards.Add(child.gameObject);
            }
            return cards;
        }

        private static void RebuildPool(DeckManager target)
        {
            VRCObjectPool pool = ResolvePool(target);
            if (pool == null)
            {
                Debug.LogError($"DeckPool: {target.name} has no VRCObjectPool - assign one to the deck's Pool field.", target);
                return;
            }

            List<GameObject> cards = CollectCards(pool);
            if (cards.Count == 0)
            {
                Debug.LogError($"DeckPool: found no card objects parented under the pool '{pool.name}'.", pool);
                return;
            }

            // Driven through SerializedProperty rather than assigning pool.Pool: this
            // is what registers the undo step and the prefab-instance override.
            SerializedObject so = new SerializedObject(pool);
            SerializedProperty array = so.FindProperty("Pool");
            if (array == null || !array.isArray)
            {
                Debug.LogError("DeckPool: no serialized 'Pool' array on VRCObjectPool; the SDK may have renamed it.", pool);
                return;
            }

            array.arraySize = cards.Count;
            for (int i = 0; i < cards.Count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
            }
            so.ApplyModifiedProperties();

            if (PrefabUtility.IsPartOfPrefabInstance(pool))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(pool);
            }

            // VRCObjectPool hands out inactive objects, so anything left active is
            // invisible to TryToSpawn and would quietly shrink the usable deck.
            int deactivated = 0;
            foreach (GameObject go in cards)
            {
                if (!go.activeSelf) continue;
                Undo.RecordObject(go, "Deactivate pool card");
                go.SetActive(false);
                deactivated++;
            }

            Debug.Log($"DeckPool: {target.name} pool set to {cards.Count} cards" +
                      (deactivated > 0 ? $", {deactivated} deactivated." : "."), target);
        }

        private static void CloneUpTo(DeckManager target, int wanted)
        {
            VRCObjectPool pool = ResolvePool(target);
            if (pool == null)
            {
                Debug.LogError($"DeckPool: {target.name} has no VRCObjectPool - assign one to the deck's Pool field.", target);
                return;
            }

            List<GameObject> cards = CollectCards(pool);
            if (cards.Count == 0)
            {
                Debug.LogError($"DeckPool: pool '{pool.name}' has no card to clone from. Add one first.", pool);
                return;
            }
            if (cards.Count >= wanted)
            {
                Debug.Log($"DeckPool: pool '{pool.name}' already has {cards.Count} cards; nothing to clone.", pool);
                return;
            }

            GameObject source = cards[0];
            GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(source) as GameObject;
            int added = 0;
            for (int i = cards.Count; i < wanted; i++)
            {
                GameObject clone;
                if (prefabSource != null)
                {
                    // Keep the prefab link so later art or component fixes still reach
                    // the cloned cards.
                    clone = (GameObject)PrefabUtility.InstantiatePrefab(prefabSource, pool.transform);
                }
                else
                {
                    clone = Object.Instantiate(source, pool.transform);
                }
                if (clone == null) break;

                clone.transform.localPosition = source.transform.localPosition;
                clone.transform.localRotation = source.transform.localRotation;
                clone.transform.localScale = source.transform.localScale;
                clone.SetActive(false);
                Undo.RegisterCreatedObjectUndo(clone, "Clone pool card");
                added++;
            }
            Debug.Log($"DeckPool: cloned {added} card(s) under the pool '{pool.name}'.", pool);
        }

        private void Refresh()
        {
            if (deck == null)
            {
                report = "";
                return;
            }

            VRCObjectPool pool = ResolvePool(deck);
            int inHierarchy = CollectCards(pool).Count;
            int inArray = pool != null && pool.Pool != null ? pool.Pool.Length : 0;
            int ranks = CardLogic.RankDefinitionsCount;

            string source = deck.Pool != null
                ? "assigned reference"
                : (pool != null ? "same-object fallback (Pool field is empty)" : "unresolved");

            List<string> lines = new List<string>();
            lines.Add($"Deck: {deck.name}");
            lines.Add($"  DeckKey       {deck.DeckKey}");
            lines.Add($"  SuitsInPlay   {deck.SuitsInPlay}");
            lines.Add($"  Pool          {(pool == null ? "<none>" : pool.name)}  [{source}]");
            if (pool != null && pool.gameObject != deck.gameObject)
            {
                lines.Add("                (pool is on a separate GameObject)");
            }
            lines.Add($"  cards under pool : {inHierarchy}");
            lines.Add($"  entries in Pool  : {inArray}");
            lines.Add("");

            if (pool == null)
            {
                lines.Add("ERROR: no VRCObjectPool resolved. Assign one to the deck's Pool field.");
            }
            else if (inArray != inHierarchy)
            {
                lines.Add($"MISMATCH: Pool holds {inArray} but {inHierarchy} cards are parented under it.");
                lines.Add("Every count in DeckManager derives from Pool.Length, so the deal will");
                lines.Add("use the array's number. Press Rebuild Pool From Children.");
            }
            else
            {
                lines.Add("OK: Pool matches the hierarchy.");
            }

            // Duplicate references are the failure this tool cannot see from counts
            // alone: an array can be the right length and still list one card twice,
            // which spawns fine until TryToSpawn hits the already-active duplicate.
            if (pool != null && pool.Pool != null)
            {
                HashSet<GameObject> seen = new HashSet<GameObject>();
                int dupes = 0;
                int nulls = 0;
                foreach (GameObject go in pool.Pool)
                {
                    if (go == null) { nulls++; continue; }
                    if (!seen.Add(go)) dupes++;
                }
                if (nulls > 0) lines.Add($"ERROR: {nulls} null entr(ies) in Pool.");
                if (dupes > 0) lines.Add($"ERROR: {dupes} duplicate reference(s) in Pool - the same card listed more than once.");
            }

            if (inHierarchy % ranks != 0)
            {
                lines.Add($"WARNING: {inHierarchy} cards is not a whole number of {ranks}-rank copies; the remainder become jokers.");
            }
            else if (deck.SuitsInPlay > 0)
            {
                int copies = inHierarchy / ranks;
                lines.Add($"  -> {copies} copies of {ranks} ranks");
                if (copies % deck.SuitsInPlay != 0)
                {
                    lines.Add($"WARNING: {copies} copies do not divide evenly into SuitsInPlay={deck.SuitsInPlay};");
                    lines.Add("some suits would be dealt more often than others.");
                }
                else
                {
                    lines.Add($"  -> {copies / deck.SuitsInPlay} copies per suit across {deck.SuitsInPlay} suit(s)");
                }
            }

            report = string.Join("\n", lines);
        }
    }
}
