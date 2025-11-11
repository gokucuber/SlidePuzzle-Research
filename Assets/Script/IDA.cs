
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;

// 8パズル ＆ 15パズル「挑戦（ちょうせん）」版 IDA*探索スクリプト
// （★★ OutOfMemoryException 修正版 ★★）
public class IDA : MonoBehaviour
{
    // ========== 1. Unity / Controller 関連 ==========
    public Button idaButton;
    public Button replayButton;
    private Controller Ctr;

    [Header("【1. 自動テストの設定】")]
    public bool isBatchTestMode = true;
    public int numTrials = 10000;

    [Header("【2. ヒューリスティクスの設定】")]
    public bool usePDB = true;

    private Dictionary<ulong, byte> patternDB1 = null;
    private Dictionary<ulong, byte> patternDB2 = null;

    // (内部で使うパズルの状態)
    private int rowNum;
    private int tileNum;
    private Vector2Int[] nowPos;
    private Vector2Int[] finPos;
    private Vector2Int[] staPos;

    // (探索の管理用)
    private List<Vector2Int[]> solutionPath = null;
    private const int INF = int.MaxValue;
    private int nodesSearched = 0;
    private float startSearch = 0f;

    // (リプレイ用)
    private bool isReplaying = false;

    // (マンハッタン距離の早見表)
    private int[,] manhattanTable = null;


    // ========== 2. IDA*アルゴリズムの本体 ==========

    private class SearchNode
    {
        public Vector2Int[] state; public int g; public int pathIndex;
        public int neighborIndex; public List<Vector2Int[]> neighbors;
        public bool isReturning; public int lastDir;
    }

    void Start()
    {
        Ctr = GetComponent<Controller>();
        if (idaButton != null) idaButton.onClick.AddListener(OnButtonClick_Start);
        if (replayButton != null)
        {
            replayButton.onClick.AddListener(OnButtonClickReplay);
            replayButton.interactable = false;
        }
    }

    public void OnButtonClick_Start()
    {
        if (isReplaying) return;
        if (Ctr == null) Ctr = GetComponent<Controller>();
        if (Ctr == null) { Debug.LogError("Controller が見つかりません。"); return; }

        StopAllCoroutines();

        if (isBatchTestMode)
        {
            StartCoroutine(RunBatchTest(numTrials, Ctr.isCountInterpretation));
        }
        else
        {
            StartCoroutine(RunSingleTest(Ctr.isCountInterpretation));
        }
    }


    // --- 「自動試行（N回）」の親コルーチン ---
    IEnumerator RunBatchTest(int totalTrials, bool isPushRule)
    {
        patternDB1 = null;
        patternDB2 = null;
        Ctr.isStop = false;

        // --- 1. テスト前の準備 ---
        string ruleName = isPushRule ? "Push" : "Standard";
        Debug.Log($"★★★ 自動テスト開始 (ルール: {ruleName}, 回数: {totalTrials}回) ★★★");
        Ctr.isCountInterpretation = isPushRule;

        // ★ Ctrから N と tileNum を“正しく”受け取る
        rowNum = Ctr.rowNum;
        tileNum = Ctr.tileNum;
        Debug.Log($"★ 取得（しゅとく）した設定（せってい）: N={rowNum}, tileNum={tileNum}");

        nowPos = new Vector2Int[Ctr.tileNum + 1];
        finPos = new Vector2Int[Ctr.tileNum + 1];
        staPos = new Vector2Int[Ctr.tileNum + 1];
        ConvertFromController();
        BuildManhattanTable();

        // --- 2. ★ PDB（攻略本）を準備する ★ ---
        if (usePDB)
        {
            yield return StartCoroutine(PreparePDB(isPushRule));
            if (rowNum == 4 && (patternDB1 == null || patternDB2 == null))
            { Debug.LogError("15パズルのPDB準備に失敗。中止します。"); yield break; }
            if (rowNum == 3 && patternDB1 == null)
            { Debug.LogError("8パズルのPDB準備に失敗。中止します。"); yield break; }
            Debug.Log("PDB準備が完了しました。");
        }
        else
        {
            Debug.LogWarning("PDB（攻略本）を使用しないでテストを実行します（h/k を使用）");
            patternDB1 = null;
            patternDB2 = null;
        }

        // --- 3. N回ループ ---
        Debug.Log("探索ループを開始します...");
        for (int i = 1; i <= totalTrials; i++)
        {
            if (Ctr.isStop) { Debug.Log("自動テストが手動で中断されました。"); yield break; }
            Ctr.Shuffle();
            ConvertFromController();
            yield return StartCoroutine(IDAStarCoroutine((Vector2Int[])staPos.Clone(), true));
            if (i % 1000 == 0) { Debug.Log($"--- 自動テスト進捗: {i} / {totalTrials} 回 完了 ---"); }
        }

        // --- 4. テスト完了 ---
        Debug.Log($"★★★ 自動テスト完了 ({totalTrials}回) ★★★");
        Debug.Log($"結果はPCの「ドキュメント」フォルダ内の CSV を確認してください。");
    }

    // --- 「手動試行（1回だけ）」の親コルーチン ---
    IEnumerator RunSingleTest(bool isPushRule)
    {
        patternDB1 = null;
        patternDB2 = null;
        Ctr.isStop = false;

        // --- 1. 準備 ---
        string ruleName = isPushRule ? "Push" : "Standard";
        Debug.Log($"★★★ 手動テスト開始 (ルール: {ruleName}) ★★★");
        Ctr.isCountInterpretation = isPushRule;

        rowNum = Ctr.rowNum;
        tileNum = Ctr.tileNum;
        Debug.Log($"★ 取得（しゅとく）した設定（せってい）: N={rowNum}, tileNum={tileNum}");

        nowPos = new Vector2Int[Ctr.tileNum + 1];
        finPos = new Vector2Int[Ctr.tileNum + 1];
        staPos = new Vector2Int[Ctr.tileNum + 1];

        ConvertFromController();
        BuildManhattanTable();

        // --- 2. ★ PDB（攻略本）を準備する ★ ---
        if (usePDB)
        {
            yield return StartCoroutine(PreparePDB(isPushRule));
            if (rowNum == 4 && (patternDB1 == null || patternDB2 == null))
            { Debug.LogError("15パズルのPDB準備に失敗。中止します。"); yield break; }
            if (rowNum == 3 && patternDB1 == null)
            { Debug.LogError("8パズルのPDB準備に失敗。中止します。"); yield break; }
            Debug.Log("PDB準備が完了しました。");
        }
        else
        {
            Debug.LogWarning("PDB（攻略本）を使用しないで探索します（h/k を使用）");
            patternDB1 = null;
            patternDB2 = null;
        }

        // --- 3. 1回だけ探索 ---
        Debug.Log("探索を開始します...");

        yield return StartCoroutine(IDAStarCoroutine((Vector2Int[])staPos.Clone(), false));
    }


    // --- IDA*探索の「親」となるコルーチン ---
    IEnumerator IDAStarCoroutine(Vector2Int[] startState, bool isBatch)
    {
        solutionPath = null;
        int threshold = Heuristic(startState, -1);
        startSearch = Time.realtimeSinceStartup;
        int iteration = 0;

        while (true)
        {
            if (Ctr.isStop) yield break;
            iteration++;
            nodesSearched = 0;
            int temp = INF;

            yield return StartCoroutine(SearchRecursive(startState, threshold, -1, result => temp = result));

            if (Ctr.isStop) yield break;

            if (temp == threshold)
            {
                if (usePDB && (patternDB1 != null || patternDB2 != null))
                {
                    yield return StartCoroutine(ReconstructPathWithPDB(startState));
                }

                if (solutionPath != null && solutionPath.Count > 0)
                {
                    float elapsed = Time.realtimeSinceStartup - startSearch;
                    int moves = solutionPath.Count - 1;
                    string rule = Ctr.isCountInterpretation ? "Push" : "Standard";
                    int pushCount = AnalyzeSolutionPath(solutionPath, Ctr.isCountInterpretation);

                    if (isBatch)
                    {
                        RecordResult(rule, moves, elapsed, nodesSearched, pushCount);
                    }
                    else
                    {
                        string ruleName = Ctr.isCountInterpretation ? "Push" : "Standard";
                        Debug.Log($"★★★ 解発見! ★★★");
                        Debug.Log($"ルール: {ruleName}, 手数: {moves}手");
                        Debug.Log($"探索時間: {elapsed:F4}秒, 探索ノード数: {nodesSearched}");
                        Debug.Log($"押し出し回数: {pushCount}回");
                        if (replayButton != null) replayButton.interactable = true;
                        yield return StartCoroutine(PlaySolution());
                    }
                }
                else
                {
                    if (!isBatch) Debug.LogError("解は見つかりましたが、経路の構築に失敗しました。");
                }

                yield break;
            }

            if (temp == INF)
            {
                if (!isBatch) Debug.LogError("解が見つかりませんでした。");
                yield break;
            }

            if (!isBatch)
            {
                Debug.Log($"反復{iteration}: 閾値={temp}, ノード数={nodesSearched}, 経過時間={(Time.realtimeSinceStartup - startSearch):F2}秒");
            }
            threshold = temp;
        }
    }

    // --- IDA*の「本体」（「再帰（さいき）」バージョン） ---
    // ★★★ OutOfMemoryException 修正版 ★★★
    IEnumerator SearchRecursive(Vector2Int[] startState, int threshold, int prevDir, System.Action<int> callback)
    {
        var path = new List<Vector2Int[]>();
        path.Add((Vector2Int[])startState.Clone());

        // ★★★ 修正点（ここから） ★★★
        // var visited = new Dictionary<ulong, int>(); // ← メモリ爆発の原因！ 削除！
        // ★★★ 修正点（ここまで） ★★★

        int minNextThreshold = INF;
        bool goalFound = false;

        System.Action<Vector2Int[], int, int> searchRecursive = null;
        searchRecursive = (currentState, g, lastDir) =>
        {
            if (goalFound || Ctr.isStop) return;
            nodesSearched++;
            int h = Heuristic(currentState, lastDir);
            int f = g + h;

            if (f > threshold)
            {
                if (f < minNextThreshold) minNextThreshold = f;
                return;
            }

            // ★★★ 修正点（ここから） ★★★
            // ulong key = StateToUlong(currentState);          // ← 削除！
            // if (visited.ContainsKey(key) && visited[key] <= g) // ← 削除！
            // {
            //     return;
            // }
            // visited[key] = g;                                 // ← 削除！
            // ★★★ 修正点（ここまで） ★★★

            if (h == 0 && SameState(currentState, finPos))
            {
                if (!usePDB || (patternDB1 == null && patternDB2 == null))
                {
                    solutionPath = new List<Vector2Int[]>(path);
                }
                goalFound = true;
                return;
            }

            var neighbors = Ctr.isCountInterpretation ?
                                GetNeighborsMultiSlide(currentState, g, lastDir) :
                                GetNeighbors1WithPruning(currentState, lastDir);

            foreach (var neighbor in neighbors)
            {
                if (goalFound || Ctr.isStop) return;
                path.Add((Vector2Int[])neighbor.Clone());
                searchRecursive(neighbor, g + 1, GetMoveDirection(currentState, neighbor));
                if (goalFound || Ctr.isStop) return;
                path.RemoveAt(path.Count - 1);
            }
        };

        searchRecursive(startState, 0, prevDir);

        if (goalFound) { callback(threshold); }
        else if (Ctr.isStop) { callback(INF); }
        else { callback(minNextThreshold); }
        yield break;
    }


    // ========== 3. PDB（攻略本）の構築・読み込み関連 ==========

    // --- PDBをファイルに保存する関数 ---
    private void SavePDB(string filePath, Dictionary<ulong, byte> pdb)
    {
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(fs, pdb);
                Debug.Log($"PDBを {filePath} に保存しました。サイズ: {pdb.Count} 件");
            }
        }
        catch (Exception e) { Debug.LogError($"PDBの保存に失敗: {e.Message}"); }
    }

    // --- PDBをファイルから読み込む関数 ---
    private Dictionary<ulong, byte> LoadPDB(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"PDBファイル {filePath} が見つかりません。");
            return null;
        }
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                Dictionary<ulong, byte> pdb = (Dictionary<ulong, byte>)formatter.Deserialize(fs);
                Debug.Log($"PDBを {filePath} から読み込みました。サイズ: {pdb.Count} 件");
                return pdb;
            }
        }
        catch (Exception e) { Debug.LogError($"PDBの読み込みに失敗: {e.Message}"); return null; }
    }

    // --- PDB準備の「親」関数（「押し出し」か「標準」かを振り分ける） ---
    IEnumerator PreparePDB(bool isPushRule)
    {
        if (patternDB1 != null || patternDB2 != null) yield break;

        if (rowNum == 3)
        {
            if (isPushRule) { yield return StartCoroutine(BuildPatternDatabase_8Puzzle_Push()); }
            else { yield return StartCoroutine(BuildPatternDatabase_8Puzzle_Standard()); }
        }
        else if (rowNum == 4)
        {
            if (isPushRule) { yield return StartCoroutine(BuildPatternDatabase_15Puzzle_Push()); }
            else { yield return StartCoroutine(BuildPatternDatabase_15Puzzle_Standard()); }
        }
    }

    // --- PDB構築の「親」関数（「押し出し」8パズル） ---
    IEnumerator BuildPatternDatabase_8Puzzle_Push()
    {
        string pdbPath = Application.persistentDataPath + "/8puzzle_full_multislide.dat";
        int[] all_tiles = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        patternDB1 = LoadPDB(pdbPath);
        int maxDepthInFile = 0;
        if (patternDB1 == null)
        {
            if (Ctr.isStop) yield break;
            Debug.Log($"8パズル「押し出し」全状態（181,440件）を新規構築します...(数分かかります)");
            patternDB1 = new Dictionary<ulong, byte>();
            yield return StartCoroutine(BuildSinglePatternDB_MultiSlide(all_tiles, patternDB1, 100));
            if (!Ctr.isStop) SavePDB(pdbPath, patternDB1);
        }
        if (patternDB1 != null && patternDB1.Count > 0)
        {
            maxDepthInFile = patternDB1.Values.Max();
            Debug.LogError($"★★★【PDB準備完了】「押し出し」ルールの最長手数は: {maxDepthInFile} 手です ★★★");
            PrintMaxDepthBoards(patternDB1, maxDepthInFile, "押し出し");
        }
    }

    // --- PDB構築の「親」関数（「標準」8パズル） ---
    IEnumerator BuildPatternDatabase_8Puzzle_Standard()
    {
        string pdbPath = Application.persistentDataPath + "/8puzzle_full_standard.dat";
        int[] all_tiles = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        patternDB1 = LoadPDB(pdbPath);
        int maxDepthInFile = 0;
        if (patternDB1 == null)
        {
            if (Ctr.isStop) yield break;
            Debug.Log($"8パズル「標準」全状態（181,440件）を新規構築します...(数秒～数十秒かかります)");
            patternDB1 = new Dictionary<ulong, byte>();
            yield return StartCoroutine(BuildSinglePatternDB_Standard(all_tiles, patternDB1, 100));
            if (!Ctr.isStop) SavePDB(pdbPath, patternDB1);
        }
        if (patternDB1 != null && patternDB1.Count > 0)
        {
            maxDepthInFile = patternDB1.Values.Max();
            Debug.LogError($"★★★【PDB準備完了】「標準」ルールの最長手数は: {maxDepthInFile} 手です ★★★");
            PrintMaxDepthBoards(patternDB1, maxDepthInFile, "標準");
        }
    }

    // --- ★★★ 15パズルPDB（押し出し）構築「親」関数 ★★★ ---
    IEnumerator BuildPatternDatabase_15Puzzle_Push()
    {
        string suffix = "_multislide";
        int[] pdb1_tiles = new int[] { 1, 2, 3, 4, 5, 6, 7 };
        string pdb1Path = Application.persistentDataPath + $"/15puzzle_1-7{suffix}.dat";
        patternDB1 = LoadPDB(pdb1Path);
        if (patternDB1 == null)
        {
            if (Ctr.isStop) yield break;
            Debug.Log("15パズル「押し出し」PDB1 (1-7) を新規構築します...（これが“あのバグ”を再現するはず！）");
            patternDB1 = new Dictionary<ulong, byte>();
            yield return StartCoroutine(BuildSinglePatternDB_MultiSlide(pdb1_tiles, patternDB1, 100));
            if (!Ctr.isStop) SavePDB(pdb1Path, patternDB1);
        }

        int[] pdb2_tiles = new int[] { 8, 9, 10, 11, 12, 13, 14, 15 };
        string pdb2Path = Application.persistentDataPath + $"/15puzzle_8-15{suffix}.dat";
        patternDB2 = LoadPDB(pdb2Path);
        if (patternDB2 == null)
        {
            if (Ctr.isStop) yield break;
            Debug.Log("15パズル「押し出し」PDB2 (8-15) を新規構築します...（こっちもバグるかも）");
            patternDB2 = new Dictionary<ulong, byte>();
            yield return StartCoroutine(BuildSinglePatternDB_MultiSlide(pdb2_tiles, patternDB2, 100));
            if (!Ctr.isStop) SavePDB(pdb2Path, patternDB2);
        }
    }

    // --- ★★★ 15パズルPDB（標準）構築「親」関数 ★★★ ---
    IEnumerator BuildPatternDatabase_15Puzzle_Standard()
    {
        string suffix = "_standard";
        int[] pdb1_tiles = new int[] { 1, 2, 3, 4, 5, 6, 7 };
        string pdb1Path = Application.persistentDataPath + $"/15puzzle_1-7{suffix}.dat";
        patternDB1 = LoadPDB(pdb1Path);
        if (patternDB1 == null)
        {
            if (Ctr.isStop) yield break;
            Debug.Log("15パズル「標準」PDB1 (1-7) を新規構築します...（数分かかります）");
            patternDB1 = new Dictionary<ulong, byte>();
            yield return StartCoroutine(BuildSinglePatternDB_Standard(pdb1_tiles, patternDB1, 100));
            if (!Ctr.isStop) SavePDB(pdb1Path, patternDB1);
        }

        int[] pdb2_tiles = new int[] { 8, 9, 10, 11, 12, 13, 14, 15 };
        string pdb2Path = Application.persistentDataPath + $"/15puzzle_8-15{suffix}.dat";
        patternDB2 = LoadPDB(pdb2Path);
        if (patternDB2 == null)
        {
            if (Ctr.isStop) yield break;
            Debug.Log("15パズル「標準」PDB2 (8-15) を新規構築します...（PDB1より時間がかかります）");
            patternDB2 = new Dictionary<ulong, byte>();
            yield return StartCoroutine(BuildSinglePatternDB_Standard(pdb2_tiles, patternDB2, 100));
            if (!Ctr.isStop) SavePDB(pdb2Path, patternDB2);
        }
    }


    // ★「押し出し」PDB構築関数（BFS本体）
    IEnumerator BuildSinglePatternDB_MultiSlide(int[] targetTiles, Dictionary<ulong, byte> pdb, int maxDepth)
    {
        Debug.LogWarning($"--- ★★★ 押し出しPDB構築（全状態BFS）が起動 ★★★ ---");
        Debug.LogError($"★ N={rowNum}, tileNum={tileNum}, maxDepth={maxDepth}, targetTiles.Length={targetTiles.Length}");
        float startTime = Time.realtimeSinceStartup;
        var queue = new Queue<(Vector2Int[], int)>();
        var goalState = (Vector2Int[])finPos.Clone();
        ulong goalKey = GetPatternKey(goalState, targetTiles);
        pdb[goalKey] = 0;
        queue.Enqueue((goalState, 0));
        long processed = 0;
        int maxDepthFound = 0;
        while (queue.Count > 0)
        {
            if (Ctr.isStop) yield break;
            var (state, depth) = queue.Dequeue();
            if (depth > maxDepthFound) { maxDepthFound = depth; Debug.Log($"★ 最長手数を更新(Push): {maxDepthFound} 手"); }
            if (depth >= maxDepth) continue;
            var neighbors = GetNeighborsMultiSlide_ForPDB(state);
            foreach (var neighbor in neighbors)
            {
                ulong key = GetPatternKey(neighbor, targetTiles);
                if (!pdb.ContainsKey(key)) { pdb[key] = (byte)(depth + 1); queue.Enqueue((neighbor, depth + 1)); }
            }
            processed++;
            if (processed % 20000 == 0) { Debug.Log($"PDB(Push)構築中... {processed}件"); yield return null; }
        }
        float elapsedTime = Time.realtimeSinceStartup - startTime;
        Debug.Log($"PDB(Push)構築完了。総件数: {processed}");
        Debug.LogError($"★★★【証明完了】「押し出し」ルールの最長手数は: {maxDepthFound} 手でした ★★★ (構築時間: {elapsedTime:F2}秒)");
    }

    // --- 「標準」PDB構築関数（BFS本体）
    IEnumerator BuildSinglePatternDB_Standard(int[] targetTiles, Dictionary<ulong, byte> pdb, int maxDepth)
    {
        Debug.LogWarning($"--- ★★★ 標準PDB構築（全状態BFS）が起動 ★★★ ---");
        Debug.LogError($"★ N={rowNum}, tileNum={tileNum}, maxDepth={maxDepth}, targetTiles.Length={targetTiles.Length}");
        float startTime = Time.realtimeSinceStartup;
        var queue = new Queue<(Vector2Int[], int)>();
        var goalState = (Vector2Int[])finPos.Clone();
        ulong goalKey = GetPatternKey(goalState, targetTiles);
        pdb[goalKey] = 0;
        queue.Enqueue((goalState, 0));
        long processed = 0;
        int maxDepthFound = 0;
        while (queue.Count > 0)
        {
            if (Ctr.isStop) yield break;
            var (state, depth) = queue.Dequeue();
            if (depth > maxDepthFound) { maxDepthFound = depth; Debug.Log($"★ 最長手数を更新(Std): {maxDepthFound} 手"); }
            if (depth >= maxDepth) continue;
            var neighbors = GetNeighbors1_ForPDB(state);
            foreach (var neighbor in neighbors)
            {
                ulong key = GetPatternKey(neighbor, targetTiles);
                if (!pdb.ContainsKey(key)) { pdb[key] = (byte)(depth + 1); queue.Enqueue((neighbor, depth + 1)); }
            }
            processed++;
            if (processed % 20000 == 0) { Debug.Log($"PDB(Std)構築中... {processed}件"); yield return null; }
        }
        float elapsedTime = Time.realtimeSinceStartup - startTime;
        Debug.Log($"PDB(Std)構築完了。総件数: {processed}");
        Debug.LogError($"★★★【証明完了】「標準」ルールの最長手数は: {maxDepthFound} 手でした ★★★ (構築時間: {elapsedTime:F2}秒)");
    }


    // ★ PDB構築（BFS）専用の「押し出し」関数
    List<Vector2Int[]> GetNeighborsMultiSlide_ForPDB(Vector2Int[] statePos)
    {
        var list = new List<Vector2Int[]>();
        int N = rowNum;
        int maxSlide = (N == 4) ? 3 : 2;
        Vector2Int empty = statePos[0];
        int[] dx = { 1, -1, 0, 0 }; int[] dy = { 0, 0, 1, -1 };
        for (int dir = 0; dir < 4; dir++)
        {
            for (int slideCount = 1; slideCount <= maxSlide; slideCount++)
            {
                bool ok = true; var tiles = new List<int>();
                for (int s = 1; s <= slideCount; s++)
                {
                    int nx = empty.x + dx[dir] * s; int ny = empty.y + dy[dir] * s;
                    if (nx < 0 || nx >= N || ny < 0 || ny >= N) { ok = false; break; }
                    int tileNum = -1;
                    for (int t = 1; t <= this.tileNum; t++)
                    {
                        if (statePos[t].x == nx && statePos[t].y == ny) { tileNum = t; break; }
                    }
                    if (tileNum <= 0) { ok = false; break; }
                    tiles.Add(tileNum);
                }
                if (!ok) continue;
                Vector2Int[] orig = (Vector2Int[])statePos.Clone();
                Vector2Int[] ns = (Vector2Int[])statePos.Clone();
                int k = tiles.Count;
                ns[0] = orig[tiles[k - 1]];
                for (int i = k - 1; i >= 1; i--) { ns[tiles[i]] = orig[tiles[i - 1]]; }
                ns[tiles[0]] = orig[0];
                list.Add(ns);
            }
        }
        return list;
    }

    // ★ PDB構築（BFS）専用の「標準」関数
    List<Vector2Int[]> GetNeighbors1_ForPDB(Vector2Int[] statePos)
    {
        var list = new List<Vector2Int[]>();
        int N = rowNum;
        Vector2Int empty = statePos[0];
        int[] dx = { 1, -1, 0, 0 }; int[] dy = { 0, 0, 1, -1 };
        for (int dir = 0; dir < 4; dir++)
        {
            int nx = empty.x + dx[dir]; int ny = empty.y + dy[dir];
            if (nx < 0 || nx >= N || ny < 0 || ny >= N) continue;
            int tileNum = -1;
            for (int t = 1; t <= this.tileNum; t++)
            {
                if (statePos[t].x == nx && statePos[t].y == ny) { tileNum = t; break; }
            }
            if (tileNum < 0) continue;
            Vector2Int[] ns = (Vector2Int[])statePos.Clone();
            Vector2Int tmp = ns[0]; ns[0] = ns[tileNum]; ns[tileNum] = tmp;
            list.Add(ns);
        }
        return list;
    }


    // ========== 4. ヒューリスティクス & 補助関数 ==========

    // ★★★ 15パズル対応版キー生成 ★★★
    ulong GetPatternKey(Vector2Int[] state, int[] targetTiles)
    {
        ulong key = 0;
        int N = rowNum;

        // 8パズル（分割なし）の場合
        if (N == 3)
        {
            for (int i = 0; i <= 8; i++) // 0～8
            {
                if (i >= state.Length || state[i] == null) continue;
                int pos = state[i].x * N + state[i].y;
                key |= ((ulong)pos << (i * 4));
            }
        }
        // 15パズル（7-8分割）の場合
        else // (N == 4)
        {
            // （PDB構築の時だけ呼ばれる）
            for (int i = 0; i < targetTiles.Length; i++) // 7回 または 8回
            {
                int tile = targetTiles[i]; // 1-7 または 8-15
                if (tile >= state.Length || state[tile] == null) continue;
                int pos = state[tile].x * N + state[tile].y; // 0～15
                key |= ((ulong)pos << (i * 4));
            }
            // ★ 空白(0)の位置もキーに含める！
            int emptyPos = state[0].x * N + state[0].y;
            key |= ((ulong)emptyPos << (targetTiles.Length * 4));
        }
        return key;
    }

    // ★★★ 15パズル対応版アンパック ★★★
    Vector2Int[] UnpackKeyToState(ulong key, int N)
    {
        Vector2Int[] state = new Vector2Int[N * N];
        for (int i = 0; i < (N * N); i++) // 0～8 または 0～15
        {
            ulong chunk = (key >> (i * 4)) & 0xF;
            int pos = (int)chunk;
            int x = pos / N;
            int y = pos % N;
            state[i] = new Vector2Int(x, y);
        }
        return state;
    }

    // ★ 盤面(state)を、人間が読める「文字列」にフォーマットする関数
    string FormatState(Vector2Int[] state, int N)
    {
        int[] grid = new int[N * N];
        for (int piece = 0; piece < state.Length; piece++)
        {
            if (piece >= state.Length || state[piece] == null) continue;
            int x = state[piece].x;
            int y = state[piece].y;
            int pos = x * N + y;
            if (pos >= 0 && pos < grid.Length) { grid[pos] = piece; }
        }
        StringBuilder boardString = new StringBuilder();
        boardString.Append($"\n--- 盤面 (N={N}) ---\n");
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                boardString.Append(grid[i * N + j].ToString().PadLeft(2) + " ");
            }
            boardString.Append("\n");
        }
        return boardString.ToString();
    }

    // ★ PDBを全探索して、最長手数の盤面を全部コンソールに出力する関数
    void PrintMaxDepthBoards(Dictionary<ulong, byte> pdb, int maxDepth, string ruleName)
    {
        Debug.LogWarning($"--- 【{ruleName}ルール】最長手数 ({maxDepth}手) の盤面を探索中... ---");
        int count = 0;

        // 8パズル（分割なし）の場合のみ実行
        if (rowNum == 3)
        {
            foreach (var pair in pdb)
            {
                if (pair.Value == maxDepth)
                {
                    ulong key = pair.Key;
                    Vector2Int[] state = UnpackKeyToState(key, rowNum);
                    string boardText = FormatState(state, rowNum);
                    Debug.Log($"【{ruleName} / {maxDepth}手 盤面 {count + 1}】 {boardText}");
                    count++;
                }
            }
        }

        if (count > 0) { Debug.LogWarning($"--- {ruleName}ルールの最長({maxDepth}手)盤面は、全部で {count} 件見つかりました ---"); }
        else if (rowNum == 3) { Debug.LogError("あれ？ 最長手数の盤面が見つかりませんでした。"); }
    }


    // --- マンハッタン距離の「早見表」を作る関数 ---
    void BuildManhattanTable()
    {
        if (manhattanTable != null) return;
        int N = rowNum;
        int tiles = tileNum;
        manhattanTable = new int[tiles + 1, N * N];
        for (int t = 1; t <= tiles; t++)
        {
            int gx = finPos[t].x;
            int gy = finPos[t].y;
            for (int px = 0; px < N; px++)
                for (int py = 0; py < N; py++)
                    manhattanTable[t, px * N + py] = Mathf.Abs(gx - px) + Mathf.Abs(gy - py);
        }
    }

    // ★★★★★★ 15パズルPDB対応版ヒューリスティクス ★★★★★★
    int Heuristic(Vector2Int[] pos, int lastDir)
    {
        // （（（★【`h_val`エラー】修正版！ ★）））

        int h_val = 0; // ヒューリスティクス値

        // --- ★ 1. PDB がロード済みの場合 ★ ---
        if (usePDB && (patternDB1 != null))
        {
            // 8パズル（分割なし）の場合
            if (rowNum == 3)
            {
                // 8パズルは「分割なし」なので、targetTiles はダミー（使われない）
                ulong key = GetPatternKey(pos, null);
                if (patternDB1.TryGetValue(key, out byte moves))
                {
                    return (int)moves; // 「答え」を返す
                }
                else { Debug.LogError($"8パズルPDBにキー {key} が見つかりません！"); }
            }
            // 15パズル（7-8分割）の場合
            else if (rowNum == 4 && patternDB2 != null)
            {
                // PDB1 (1-7 + 空白)
                ulong key1 = GetPatternKey(pos, new int[] { 1, 2, 3, 4, 5, 6, 7 });
                int pdb1Val = patternDB1.TryGetValue(key1, out byte v1) ? v1 : 0;

                // PDB2 (8-15 + 空白)
                ulong key2 = GetPatternKey(pos, new int[] { 8, 9, 10, 11, 12, 13, 14, 15 });
                int pdb2Val = patternDB2.TryGetValue(key2, out byte v2) ? v2 : 0;

                // ↓↓↓ ★★★【「h_val」エラー修正！】★★★ ↓↓↓
                // ★ 2つのPDBの“足し算”を「予測」として h_val に“代入”！
                h_val = pdb1Val + pdb2Val;
                // ↑↑↑ ★★★★★★★★★★★★★★★★★★★ ↑↑↑
            }
        }

        // --- ★ 2. PDBが「ない」場合、または15パズルのPDB予測を「h/k」する場合 ★ ---
        if (h_val == 0) // ★ PDBを使わなかった時だけ、マンハッタンを計算
        {
            int N = rowNum;
            for (int t = 1; t <= tileNum; t++)
            {
                int idx = pos[t].x * N + pos[t].y;
                h_val += manhattanTable[t, idx];
            }
            h_val += LinearConflict(pos);
        }

        // --- ★ 3. 「押し出し」ルールなら、h/k で割る ★ ---
        if (Ctr.isCountInterpretation)
        {
            int maxSlide = (rowNum == 4) ? 3 : 2;
            if (maxSlide > 0 && h_val > 0)
            {
                return (int)Mathf.Ceil((float)h_val / (float)maxSlide);
            }
            else { return h_val; }
        }
        else { return h_val; } // 標準ルールの場合
    }

    // --- 線形コンフリクト(Linear Conflict)を計算する ---
    int LinearConflict(Vector2Int[] pos)
    {
        int add = 0; int N = rowNum;
        for (int r = 0; r < N; r++)
        {
            var rowTiles = new List<int>();
            for (int t = 1; t <= tileNum; t++)
                if (pos[t].x == r && finPos[t].x == r) rowTiles.Add(t);
            for (int a = 0; a < rowTiles.Count; a++)
                for (int b = a + 1; b < rowTiles.Count; b++)
                {
                    int ta = rowTiles[a]; int tb = rowTiles[b];
                    if (pos[ta].y < pos[tb].y && finPos[ta].y > finPos[tb].y) add += 2;
                }
        }
        for (int c = 0; c < N; c++)
        {
            var colTiles = new List<int>();
            for (int t = 1; t <= tileNum; t++)
                if (pos[t].y == c && finPos[t].y == c) colTiles.Add(t);
            for (int a = 0; a < colTiles.Count; a++)
                for (int b = a + 1; b < colTiles.Count; b++)
                {
                    int ta = colTiles[a]; int tb = colTiles[b];
                    if (pos[ta].x < pos[tb].x && finPos[ta].x > finPos[tb].x) add += 2;
                }
        }
        return add;
    }

    // --- 「標準ルール」：空白の1マス移動（IDA*探索用） ---
    List<Vector2Int[]> GetNeighbors1WithPruning(Vector2Int[] statePos, int lastDir)
    {
        if (usePDB && (patternDB1 != null || patternDB2 != null))
        {
            return GetNeighbors1_ForPDB(statePos);
        }

        var list = new List<Vector2Int[]>();
        int N = rowNum; Vector2Int empty = statePos[0];
        int[] dx = { 1, -1, 0, 0 }; int[] dy = { 0, 0, 1, -1 };
        int oppositeDir = GetOppositeDirection(lastDir);
        for (int dir = 0; dir < 4; dir++)
        {
            if (dir == oppositeDir) continue;
            int nx = empty.x + dx[dir]; int ny = empty.y + dy[dir];
            if (nx < 0 || nx >= N || ny < 0 || ny >= N) continue;
            int tileNum = -1;
            for (int t = 1; t <= this.tileNum; t++)
            {
                if (statePos[t].x == nx && statePos[t].y == ny) { tileNum = t; break; }
            }
            if (tileNum < 0) continue;
            Vector2Int[] ns = (Vector2Int[])statePos.Clone();
            Vector2Int tmp = ns[0]; ns[0] = ns[tileNum]; ns[tileNum] = tmp;
            list.Add(ns);
        }
        return list;
    }

    // --- 「押し出し」ルール：複数枚を動かす（IDA*探索用） ---
    List<Vector2Int[]> GetNeighborsMultiSlide(Vector2Int[] statePos, int g, int lastDir)
    {
        if (usePDB && (patternDB1 != null || patternDB2 != null))
        {
            return GetNeighborsMultiSlide_ForPDB(statePos);
        }

        var candidates = new List<(Vector2Int[], int)>();
        int N = rowNum; int maxSlide = (N == 4) ? 3 : 2;
        Vector2Int empty = statePos[0];
        int[] dx = { 1, -1, 0, 0 }; int[] dy = { 0, 0, 1, -1 };
        int oppositeDir = GetOppositeDirection(lastDir);
        for (int dir = 0; dir < 4; dir++)
        {
            if (dir == oppositeDir) continue;
            for (int slideCount = 1; slideCount <= maxSlide; slideCount++)
            {
                bool ok = true; var tiles = new List<int>();
                for (int s = 1; s <= slideCount; s++)
                {
                    int nx = empty.x + dx[dir] * s; int ny = empty.y + dy[dir] * s;
                    if (nx < 0 || nx >= N || ny < 0 || ny >= N) { ok = false; break; }
                    int tileNum = -1;
                    for (int t = 1; t <= this.tileNum; t++)
                    {
                        if (statePos[t].x == nx && statePos[t].y == ny) { tileNum = t; break; }
                    }
                    if (tileNum <= 0) { ok = false; break; }
                    tiles.Add(tileNum);
                }
                if (!ok) continue;
                Vector2Int[] orig = (Vector2Int[])statePos.Clone();
                Vector2Int[] ns = (Vector2Int[])statePos.Clone();
                int k = tiles.Count;
                ns[0] = orig[tiles[k - 1]];
                for (int i = k - 1; i >= 1; i--) { ns[tiles[i]] = orig[tiles[i - 1]]; }
                ns[tiles[0]] = orig[0];
                int h = Heuristic(ns, dir); int f = g + 1 + h;
                candidates.Add((ns, f));
            }
        }
        candidates.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return candidates.Select(c => c.Item1).ToList();
    }


    // ========== 5. CSV書き出し & 「解の分析」 & 補助関数 ==========

    // --- ★★★ 15パズル対応版「解の分析」 ★★★ ---
    private int AnalyzeSolutionPath(List<Vector2Int[]> path, bool isPushRule)
    {
        int pushCount = 0;
        if (isPushRule)
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2Int[] prevState = path[i];
                Vector2Int[] nextState = path[i + 1];
                int changedTiles = 0;
                for (int piece = 0; piece <= tileNum; piece++) // ★ tileNum (8 or 15) まで
                {
                    if (prevState[piece] != nextState[piece]) { changedTiles++; }
                }
                if (changedTiles > 2) { pushCount++; }
            }
        }
        else
        {
            for (int i = 0; i < path.Count - 2; i++)
            {
                int dir1 = GetMoveDirection(path[i], path[i + 1]);
                int dir2 = GetMoveDirection(path[i + 1], path[i + 2]);
                if (dir1 != -1 && dir1 == dir2) { pushCount++; i++; }
            }
        }
        return pushCount;
    }


    // --- CSV書き出し関数（★ 15パズル対応版） ---
    private void RecordResult(string ruleType, int moves, float time, int nodes, int pushCount)
    {
        string ruleName = ruleType + (usePDB ? "_PDB" : "_NoPDB");
        string fileName = (rowNum == 3) ? "8puzzle_results.csv" : "15puzzle_results.csv";
        fileName = fileName.Replace(".csv", (usePDB ? "_PDB.csv" : "_NoPDB.csv"));
        string filePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "/" + fileName;

        string line = $"\"{ruleName}\",{moves},{time},{nodes},{pushCount}";
        try
        {
            if (!File.Exists(filePath))
            {
                string header = "\"Rule\",\"Moves\",\"Time(sec)\",\"Nodes\",\"PushCount\"" + Environment.NewLine;
                File.WriteAllText(filePath, header, Encoding.UTF8);
            }
            File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception e) { Debug.LogError($"CSV書き込み失敗: {e.Message}"); }
    }


    // --- ★★★ バグ修正済みの ConvertFromController ★★★ ---
    void ConvertFromController()
    {
        int count = 0;
        for (int i = 0; i < rowNum; i++)
        {
            for (int j = 0; j < rowNum; j++)
            {
                int now = Ctr.nowState[count];
                int fin = Ctr.finishState[count];
                nowPos[now] = new Vector2Int(i, j);
                finPos[fin] = new Vector2Int(i, j);
                staPos[now] = new Vector2Int(i, j);
                count++;
            }
        }
    }

    // --- 盤面の状態(Vector2Int[])を、HashSetで使えるユニークなキー（ulong型）に変換 ---
    ulong StateToUlong(Vector2Int[] state)
    {
        int N = rowNum;
        ulong key = 0ul;
        for (int i = 0; i < state.Length && i <= tileNum; i++)
        {
            if (i >= state.Length || state[i] == null) continue;
            int posIndex = state[i].x * N + state[i].y;
            ulong v = (ulong)(posIndex & 0xF);
            key |= (v << (i * 4));
        }
        return key;
    }

    // --- 2つの盤面が（ピースの配置が）完全に一致するかどうかを判定 ---
    bool SameState(Vector2Int[] a, Vector2Int[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    // --- 空白がどの方向に動いたかを 0-3 の数値で返す ---
    int GetMoveDirection(Vector2Int[] prevState, Vector2Int[] nextState)
    {
        Vector2Int prevEmpty = prevState[0]; Vector2Int nextEmpty = nextState[0];
        int dx = nextEmpty.x - prevEmpty.x; int dy = nextEmpty.y - prevEmpty.y;
        if (dx > 0) return 0; if (dx < 0) return 1; if (dy > 0) return 2; if (dy < 0) return 3;
        return -1;
    }

    // --- 方向(dir)の逆方向を返す ---
    int GetOppositeDirection(int dir)
    {
        if (dir == 0) return 1; if (dir == 1) return 0; if (dir == 2) return 3; if (dir == 3) return 2;
        return -1;
    }

    // --- 「リプレイ」ボタンが押されたときの処理 ---
    public void OnButtonClickReplay()
    {
        if (solutionPath == null || solutionPath.Count == 0)
        { Debug.Log("再生する解がありません"); return; }
        if (isReplaying)
        { Debug.Log("既にリプレイ中です"); return; }
        StartCoroutine(ReplaySolution());
    }

    // --- 解の再生（Controllerに盤面を渡す）---
    IEnumerator PlaySolution()
    {
        Debug.Log($"解を再生します... 総手数: {solutionPath.Count - 1}");
        for (int i = 0; i < solutionPath.Count; i++)
        {
            if (Ctr.isStop) break;
            Ctr.stateTileIDA(solutionPath[i], i);
            if (i < solutionPath.Count - 1)
                yield return new WaitForSeconds(0.5f);
        }
        if (!Ctr.isStop) { Ctr.finCount = 2; Debug.Log("解の再生完了！"); }
    }

    // --- リプレイの再生 ---
    IEnumerator ReplaySolution()
    {
        Ctr.isStart = true; Ctr.isfinish = false; isReplaying = true;
        Debug.Log($"リプレイを開始します... 総手数: {solutionPath.Count - 1}");
        Ctr.clickCount = 0;
        for (int i = 0; i < solutionPath.Count; i++)
        {
            if (Ctr.isStop) { Debug.Log("リプレイが中断されました"); break; }
            Ctr.stateTileIDA(solutionPath[i], i);
            if (i < solutionPath.Count - 1)
                yield return new WaitForSeconds(0.5f);
        }
        if (!Ctr.isStop) { Debug.Log("リプレイ完了！"); }
        Ctr.isfinish = true; isReplaying = false;
    }

    // --- ★★★【「経路（けいろ）バグ」修正】★★★ ---
    // PDB（攻略本）を“地図”にして、スタートからゴールまでの“最短経路”を“作り直す”関数
    IEnumerator ReconstructPathWithPDB(Vector2Int[] startState)
    {
        if (solutionPath != null && solutionPath.Count > 1) { yield break; }
        Debug.LogWarning("★ PDBモード：解の経路を再構築します...");
        var newPath = new List<Vector2Int[]>();
        newPath.Add((Vector2Int[])startState.Clone());
        var current = (Vector2Int[])startState.Clone();
        bool isPushRule = Ctr.isCountInterpretation;

        for (int i = 0; i < 200; i++)
        {
            int h_current = Heuristic(current, -1);
            if (h_current == 0) { break; } // ゴール！

            var neighbors = isPushRule ?
                                GetNeighborsMultiSlide_ForPDB(current) :
                                GetNeighbors1_ForPDB(current);

            Vector2Int[] bestPushMove = null;
            Vector2Int[] bestOneTileMove = null;

            foreach (var neighbor in neighbors)
            {
                int h_neighbor = Heuristic(neighbor, -1);
                if (h_neighbor == h_current - 1)
                {
                    if (isPushRule)
                    {
                        int dist = Mathf.Abs(current[0].x - neighbor[0].x) + Mathf.Abs(current[0].y - neighbor[0].y);
                        if (dist > 1)
                        {
                            bestPushMove = neighbor;
                            break;
                        }
                        else { bestOneTileMove = neighbor; }
                    }
                    else { bestOneTileMove = neighbor; break; }
                }
            }
            Vector2Int[] chosenMove = bestPushMove;
            if (chosenMove == null) { chosenMove = bestOneTileMove; }

            if (chosenMove != null)
            {
                newPath.Add((Vector2Int[])chosenMove.Clone());
                current = (Vector2Int[])chosenMove.Clone();
            }
            else
            {
                Debug.LogError($"経路の再構築に失敗しました (h={h_current})");
                yield break;
            }
        }
        solutionPath = newPath;
        Debug.LogWarning($"★ 経路の再構築が完了しました。総手数: {solutionPath.Count - 1}");
        yield return null;
    }
}