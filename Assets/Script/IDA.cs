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
// 8パズル専用IDA*探索スクリプト（★★ 最終・万能型 ★★）
public class IDA : MonoBehaviour
{
    // ========== 1. Unity / Controller 関連 ==========
    public Button idaButton;
    public Button replayButton;
    private Controller Ctr;

    // --- ★★★ 3つの「切り替えスイッチ」 ★★★ ---
    [Header("【1. 自動テストの設定】")]
    public bool isBatchTestMode = true; // true = 1万回テスト / false = 1回だけ解いて再生
    public int numTrials = 10000; // 試行回数 (isBatchTestMode が true の時だけ使われる)

    [Header("【2. ヒューリスティクスの設定】")]
    public bool usePDB = true; // true = PDB(攻略本)を使う / false = h/k (ヘボいコンパス) を使う
                               // --- ★★★★★★★★★★★★★★★★★ ---

    // (PDB本体)
    private Dictionary<ulong, byte> patternDB1 = null;

    // (内部で使うパズルの状態)
    private int rowNum;
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

    // IDA*の探索で使う「ノード（状態）」をスタックに積むためのクラス
    private class SearchNode
    {
        public Vector2Int[] state; public int g; public int pathIndex;
        public int neighborIndex; public List<Vector2Int[]> neighbors;
        public bool isReturning; public int lastDir;
    }

    // --- Unityの起動時に1回だけ呼ばれる ---
    void Start()
    {
        Ctr = GetComponent<Controller>();
        // ボタンが押されたら、モード（自動/手動）を切り替える親関数を呼ぶ
        if (idaButton != null) idaButton.onClick.AddListener(OnButtonClick_Start);

        if (replayButton != null)
        {
            replayButton.onClick.AddListener(OnButtonClickReplay);
            replayButton.interactable = false;
        }
    }

    // --- 「探索開始」ボタンが押されたときの処理 ---
    // (★★ 改造 ★★ 自動か手動かを、フラグで切り替える)
    public void OnButtonClick_Start()
    {
        if (isReplaying) return; // リプレイ中は実行しない
        if (Ctr == null) Ctr = GetComponent<Controller>();
        if (Ctr == null) { Debug.LogError("Controller が見つかりません。"); return; }

        StopAllCoroutines(); // 実行中のテストを停止

        // ★ Inspectorの「isBatchTestMode」フラグを見て、どっちを起動するか決める
        if (isBatchTestMode)
        {
            // --- 1. 自動テスト（1万回）モード ---
            // Ctr.isCountInterpretation (押し出し/標準) の状態を見て、テストを開始
            StartCoroutine(RunBatchTest(numTrials, Ctr.isCountInterpretation));
        }
        else
        {
            // --- 2. 手動テスト（1回だけ解いて再生）モード ---
            // Ctr.isCountInterpretation (押し出し/標準) の状態を見て、テストを開始
            StartCoroutine(RunSingleTest(Ctr.isCountInterpretation));
        }
    }


    // --- 「自動試行（1万回）」の親コルーチン ---
    IEnumerator RunBatchTest(int totalTrials, bool isPushRule)
    {
        patternDB1 = null; // ★ PDBの“記憶”をリセット！
        Ctr.isStop = false;
        // --- 1. テスト前の準備 ---
        string ruleName = isPushRule ? "Push" : "Standard";
        Debug.Log($"★★★ 自動テスト開始 (ルール: {ruleName}, 回数: {totalTrials}回) ★★★");
        Ctr.isCountInterpretation = isPushRule;
        rowNum = Ctr.rowNum;
        nowPos = new Vector2Int[Ctr.tileNum + 1];
        finPos = new Vector2Int[Ctr.tileNum + 1];
        staPos = new Vector2Int[Ctr.tileNum + 1];
        ConvertFromController();
        BuildManhattanTable();

        // --- 2. ★ PDB（攻略本）を準備する ★ ---
        // (「usePDB」フラグが true の時だけ、PDBをロード/構築する)
        if (usePDB)
        {
            yield return StartCoroutine(PreparePDB(isPushRule)); // ★ PDB準備関数を呼ぶ
            if (patternDB1 == null) { Debug.LogError("PDBの準備に失敗。中止します。"); yield break; }
            Debug.Log("PDB準備が完了しました。");
        }
        else
        {
            Debug.LogWarning("PDB（攻略本）を使用しないでテストを実行します（h/k を使用）");
            patternDB1 = null; // PDBを「使わない」ことを明示
        }

        // --- 3. 1万回ループ ---
        Debug.Log("探索ループを開始します...");
        for (int i = 1; i <= totalTrials; i++)
        {
            if (Ctr.isStop) { Debug.Log("自動テストが手動で中断されました。"); yield break; }
            Ctr.Shuffle();
            ConvertFromController();
            yield return StartCoroutine(IDAStarCoroutine((Vector2Int[])staPos.Clone(), true)); // ★ true = 自動モード
            if (i % 1000 == 0) { Debug.Log($"--- 自動テスト進捗: {i} / {totalTrials} 回 完了 ---"); }
        }

        // --- 4. テスト完了 ---
        Debug.Log($"★★★ 自動テスト完了 ({totalTrials}回) ★★★");
        Debug.Log($"結果はPCの「ドキュメント」フォルダ内の CSV を確認してください。");
    }

    // --- ★★★ 新設 ★★★ ---
    // 「手動試行（1回だけ）」の親コルーチン
    IEnumerator RunSingleTest(bool isPushRule)
    {
        patternDB1 = null; // ★ PDBの“記憶”をリセット！
        Ctr.isStop = false;
        // --- 1. 準備 ---
        string ruleName = isPushRule ? "Push" : "Standard";
        Debug.Log($"★★★ 手動テスト開始 (ルール: {ruleName}) ★★★");
        Ctr.isCountInterpretation = isPushRule;
        rowNum = Ctr.rowNum;
        nowPos = new Vector2Int[Ctr.tileNum + 1];
        finPos = new Vector2Int[Ctr.tileNum + 1];
        staPos = new Vector2Int[Ctr.tileNum + 1];

        // ★手動の時は、「今見えてる盤面」をスタート盤面にする
        // （シャッフル（Ctr.Shuffle()）は“しない”）
        ConvertFromController();

        BuildManhattanTable();

        // --- 2. ★ PDB（攻略本）を準備する ★ ---
        // (「usePDB」フラグが true の時だけ、PDBをロード/構築する)
        if (usePDB)
        {
            yield return StartCoroutine(PreparePDB(isPushRule));
            if (patternDB1 == null) { Debug.LogError("PDBの準備に失敗。中止します。"); yield break; }
            Debug.Log("PDB準備が完了しました。");
        }
        else
        {
            Debug.LogWarning("PDB（攻略本）を使用しないで探索します（h/k を使用）");
            patternDB1 = null; // PDBを「使わない」ことを明示
        }

        // --- 3. 1回だけ探索 ---
        Debug.Log("探索を開始します...");

        // ★ false = 手動モード（＝解を再生する）
        yield return StartCoroutine(IDAStarCoroutine((Vector2Int[])staPos.Clone(), false));

        // --- 4. 完了（結果はIDAStarCoroutineの中で再生される） ---
    }


    // --- IDA*探索の「親」となるコルーチン ---
    // (★★ 改造 ★★ isBatch（自動モードか？）フラグを受け取る)
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
            yield return StartCoroutine(SearchIterative(startState, threshold, -1, result => temp = result));
            if (Ctr.isStop) yield break;

            // 5. ★ 解が見つかった場合
            if (solutionPath != null)
            {
                float elapsed = Time.realtimeSinceStartup - startSearch;
                int moves = solutionPath.Count - 1;
                string rule = Ctr.isCountInterpretation ? "Push" : "Standard";
                int pushCount = AnalyzeSolutionPath(solutionPath, Ctr.isCountInterpretation);

                // ★「自動テスト」の時だけ、CSVに記録
                if (isBatch)
                {
                    RecordResult(rule, moves, elapsed, nodesSearched, pushCount);
                }
                else // ★「手動テスト」の時は、ログに出して、解を再生する
                {

                    string ruleName = Ctr.isCountInterpretation ? "Push" : "Standard";

                    Debug.Log($"★★★ 解発見! ★★★");
                    Debug.Log($"ルール: {ruleName}, 手数: {moves}手");
                    Debug.Log($"探索時間: {elapsed:F4}秒, 探索ノード数: {nodesSearched}");
                    Debug.Log($"押し出し回数: {pushCount}回");

                    if (replayButton != null) replayButton.interactable = true;
                    yield return StartCoroutine(PlaySolution()); // ★解を再生
                }

                yield break;
            }
            if (temp == INF)
            {
                if (!isBatch) Debug.LogError("解が見つかりませんでした。");
                yield break;
            }

            // 閾値更新ログ（手動モードの時だけ出す）
            if (!isBatch)
            {
                Debug.Log($"反復{iteration}: 閾値={temp}, ノード数={nodesSearched}, 経過時間={(Time.realtimeSinceStartup - startSearch):F2}秒");
            }
            threshold = temp;
        }
    }

    // --- IDA*の「本体」 (SearchIterative) --- (変更なし)
    IEnumerator SearchIterative(Vector2Int[] startState, int threshold, int prevDir, System.Action<int> callback)
    {
        var stack = new Stack<SearchNode>();
        var visited = new HashSet<ulong>();
        int minNextThreshold = INF;
        var nodePaths = new Dictionary<int, List<Vector2Int[]>>();
        int nodeIdCounter = 0;
        var initialPath = new List<Vector2Int[]> { (Vector2Int[])startState.Clone() };
        nodePaths[nodeIdCounter] = initialPath;

        stack.Push(new SearchNode
        {
            state = (Vector2Int[])startState.Clone(),
            g = 0,
            pathIndex = nodeIdCounter,
            neighborIndex = 0,
            neighbors = null,
            isReturning = false,
            lastDir = prevDir
        });
        nodeIdCounter++;

        int operationCount = 0;

        while (stack.Count > 0)
        {
            if (Ctr.isStop) { callback(INF); yield break; }

            operationCount++;
            if (operationCount % 50000 == 0) { yield return null; }

            var node = stack.Pop();

            if (node.isReturning)
            {
                if (nodePaths.ContainsKey(node.pathIndex)) nodePaths.Remove(node.pathIndex);
                visited.Remove(StateToUlong(node.state));
                continue;
            }

            if (node.neighborIndex == 0)
            {
                nodesSearched++;
                ulong key = StateToUlong(node.state);
                if (visited.Contains(key)) { continue; }
                visited.Add(key);

                int h = Heuristic(node.state, node.lastDir);
                int f = node.g + h;

                if (f > threshold)
                {
                    visited.Remove(key);
                    if (f < minNextThreshold) minNextThreshold = f;
                    continue;
                }

                if (h == 0 && SameState(node.state, finPos))
                {
                    solutionPath = new List<Vector2Int[]>();
                    var currentPath = nodePaths[node.pathIndex];
                    foreach (var p in currentPath) solutionPath.Add((Vector2Int[])p.Clone());
                    callback(node.g);
                    yield break;
                }

                if (Ctr.isCountInterpretation)
                {
                    node.neighbors = GetNeighborsMultiSlide(node.state, node.g, node.lastDir);
                }
                else
                {
                    node.neighbors = GetNeighbors1WithPruning(node.state, node.lastDir);
                }
            }

            if (node.neighborIndex < node.neighbors.Count)
            {
                var nextState = node.neighbors[node.neighborIndex]; node.neighborIndex++;
                int moveDir = GetMoveDirection(node.state, nextState);
                var currentPath = nodePaths[node.pathIndex];
                var newPath = new List<Vector2Int[]>(currentPath);
                newPath.Add((Vector2Int[])nextState.Clone());
                int newNodeId = nodeIdCounter++;
                nodePaths[newNodeId] = newPath;
                stack.Push(node);
                stack.Push(new SearchNode { state = nextState, g = node.g + 1, pathIndex = newNodeId, neighborIndex = 0, neighbors = null, isReturning = true, lastDir = moveDir });
                stack.Push(new SearchNode { state = nextState, g = node.g + 1, pathIndex = newNodeId, neighborIndex = 0, neighbors = null, isReturning = false, lastDir = moveDir });
            }
            else
            {
                if (nodePaths.ContainsKey(node.pathIndex)) nodePaths.Remove(node.pathIndex);
                visited.Remove(StateToUlong(node.state));
            }
        }
        callback(minNextThreshold);
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

    // --- ★★★ 新設 ★★★ ---
    // PDB準備の「親」関数（「押し出し」か「標準」かを振り分ける）
    IEnumerator PreparePDB(bool isPushRule)
    {
        // 既にロード済みなら何もしない
        if (patternDB1 != null) yield break;

        if (isPushRule)
        {
            yield return StartCoroutine(BuildPatternDatabase_ForPushRule());
        }
        else
        {
            yield return StartCoroutine(BuildPatternDatabase_ForStandardRule());
        }
    }

    // --- PDB構築の「親」関数（「押し出し」ルール専用） ---
    IEnumerator BuildPatternDatabase_ForPushRule()
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

    // --- PDB構築の「親」関数（「標準」ルール専用） ---
    IEnumerator BuildPatternDatabase_ForStandardRule()
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


    // ★「押し出し」PDB構築関数（＝全状態の最短手数(答え)をBFSで計算）
    IEnumerator BuildSinglePatternDB_MultiSlide(int[] targetTiles, Dictionary<ulong, byte> pdb, int maxDepth)
    {
        Debug.LogWarning($"--- ★★★ 押し出しPDB構築（全状態BFS）が起動 ★★★ ---");
        Debug.LogError($"★ N={rowNum}, tileNum={Ctr.tileNum}, maxDepth={maxDepth}, targetTiles.Length={targetTiles.Length}");

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
            var neighbors = GetNeighborsMultiSlide_ForPDB(state); // ★押し出し
            foreach (var neighbor in neighbors)
            {
                ulong key = GetPatternKey(neighbor, targetTiles);
                if (!pdb.ContainsKey(key)) { pdb[key] = (byte)(depth + 1); queue.Enqueue((neighbor, depth + 1)); }
            }
            processed++;
            if (processed % 20000 == 0) { Debug.Log($"PDB(Push)構築中... {processed}件"); yield return null; }
        }
        Debug.Log($"PDB(Push)構築完了。総件数: {processed}");
        Debug.LogError($"★★★【証明完了】「押し出し」ルールの最長手数は: {maxDepthFound} 手でした ★★★");
    }

    // --- 「標準」PDB構築関数（＝全状態の最短手数(答え)をBFSで計算）
    IEnumerator BuildSinglePatternDB_Standard(int[] targetTiles, Dictionary<ulong, byte> pdb, int maxDepth)
    {
        Debug.LogWarning($"--- ★★★ 標準PDB構築（全状態BFS）が起動 ★★★ ---");
        Debug.LogError($"★ N={rowNum}, tileNum={Ctr.tileNum}, maxDepth={maxDepth}, targetTiles.Length={targetTiles.Length}");

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

            var neighbors = GetNeighbors1_ForPDB(state); // ★標準

            foreach (var neighbor in neighbors)
            {
                ulong key = GetPatternKey(neighbor, targetTiles);
                if (!pdb.ContainsKey(key)) { pdb[key] = (byte)(depth + 1); queue.Enqueue((neighbor, depth + 1)); }
            }
            processed++;
            if (processed % 20000 == 0) { Debug.Log($"PDB(Std)構築中... {processed}件"); yield return null; }
        }
        Debug.Log($"PDB(Std)構築完了。総件数: {processed}");
        Debug.LogError($"★★★【証明完了】「標準」ルールの最長手数は: {maxDepthFound} 手でした ★★★");
    }


    // ★ PDB構築（BFS）専用の「押し出し」関数
    List<Vector2Int[]> GetNeighborsMultiSlide_ForPDB(Vector2Int[] statePos)
    {
        var list = new List<Vector2Int[]>();
        int N = rowNum;
        int maxSlide = (N == 4) ? 3 : 2; // N=3 の時は 2
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
                    for (int t = 1; t <= Ctr.tileNum; t++)
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
            for (int t = 1; t <= Ctr.tileNum; t++)
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

    // ★ 8パズル（9ピース）の全状態をキーにする関数
    ulong GetPatternKey(Vector2Int[] state, int[] targetTiles)
    {
        ulong key = 0;
        int N = rowNum; // 3
        // targetTiles（引数）を無視して、0～8 の全ピースをキーにする
        for (int i = 0; i <= 8; i++) // 9回ループ
        {
            if (i >= state.Length || state[i] == null) continue; // 安全装置
            int pos = state[i].x * N + state[i].y; // 0～8
            key |= ((ulong)pos << (i * 4)); // 9ピース x 4bit = 36bit
        }
        return key;
    }

    // ★ PDBキー(ulong)を、人間が読める「盤面(Vector2Int[])」に“アンパック”する関数
    Vector2Int[] UnpackKeyToState(ulong key, int N)
    {
        Vector2Int[] state = new Vector2Int[N * N]; // 9要素 (0～8)
        for (int i = 0; i <= 8; i++) // 9ピース (0～8)
        {
            ulong chunk = (key >> (i * 4)) & 0xF;
            int pos = (int)chunk;
            int x = pos / N;
            int y = pos % N;
            state[i] = new Vector2Int(x, y);
        }
        return state;
    }

    // ★ 盤面(state)を、人間が読める「3x3の文字列」にフォーマットする関数
    string FormatState(Vector2Int[] state, int N)
    {
        int[] grid = new int[N * N]; // 9マス
        for (int piece = 0; piece < state.Length; piece++)
        {
            if (piece >= state.Length || state[piece] == null) continue;
            int x = state[piece].x;
            int y = state[piece].y;
            int pos = x * N + y;
            if (pos >= 0 && pos < grid.Length) { grid[pos] = piece; }
        }
        StringBuilder boardString = new StringBuilder();
        boardString.Append("\n--- 盤面 ---\n");
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                boardString.Append(grid[i * N + j].ToString() + " ");
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
        if (count > 0) { Debug.LogWarning($"--- {ruleName}ルールの最長({maxDepth}手)盤面は、全部で {count} 件見つかりました ---"); }
        else { Debug.LogError("あれ？ 最長手数の盤面が見つかりませんでした。"); }
    }


    // --- マンハッタン距離の「早見表」を作る関数 ---
    void BuildManhattanTable()
    {
        if (manhattanTable != null) return;
        int N = rowNum;
        int tiles = Ctr.tileNum; // 8
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

    // ★★★★★★ PDB（攻略本）対応版ヒューリスティクス ★★★★★★
    int Heuristic(Vector2Int[] pos, int lastDir)
    {
        // --- ★ 1. PDB がロード済みの場合 ★ ---
        // (「usePDB」フラグがONで、PDBがロード成功してたら)
        if (usePDB && patternDB1 != null)
        {
            ulong key = GetPatternKey(pos, null); // 9ピースのキーを取得
            if (patternDB1.TryGetValue(key, out byte moves))
            {
                return (int)moves; // 「答え」を返す
            }
            else { Debug.LogError($"PDBにキー {key} が見つかりません！"); }
        }

        // --- ★ 2. PDBが「ない」場合（usePDBがfalse、または構築失敗） ★ ---
        int N = rowNum;
        int h_val = 0;
        for (int t = 1; t <= Ctr.tileNum; t++)
        {
            int idx = pos[t].x * N + pos[t].y;
            h_val += manhattanTable[t, idx];
        }
        h_val += LinearConflict(pos);

        if (Ctr.isCountInterpretation) // 押し出しルールの場合
        {
            int maxSlide = (N == 4) ? 3 : 2; // N=3 の時は 2
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
            for (int t = 1; t <= Ctr.tileNum; t++)
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
            for (int t = 1; t <= Ctr.tileNum; t++)
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
        // ★PDBがロード済みなら、f値ソートは不要（hが答えなので）
        if (usePDB && patternDB1 != null)
        {
            return GetNeighbors1_ForPDB(statePos); // ソートしないBFS版を流用
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
            for (int t = 1; t <= Ctr.tileNum; t++)
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
        // ★PDBがロード済みなら、f値ソートは不要（hが答えなので）
        if (usePDB && patternDB1 != null)
        {
            // PDBがある場合は、ソートを省略したBFS版を流用する
            return GetNeighborsMultiSlide_ForPDB(statePos);
        }

        // --- PDBが「ない」場合（＝usePDBがfalse、または構築中）は、従来通りf値でソートする ---
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
                    for (int t = 1; t <= Ctr.tileNum; t++)
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

    // --- 「解の手順(solutionPath)」を分析して、「押し出し回数」をカウントする関数 ---
    private int AnalyzeSolutionPath(List<Vector2Int[]> path, bool isPushRule)
    {
        int pushCount = 0;

        if (isPushRule)
        {
            // --- 「押し出し」ルールの場合 ---
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2Int emptyA = path[i][0];
                Vector2Int emptyB = path[i + 1][0];
                int dist = Mathf.Abs(emptyA.x - emptyB.x) + Mathf.Abs(emptyA.y - emptyB.y);
                if (dist > 1) { pushCount++; }
            }
        }
        else
        {
            // --- 「標準」ルールの場合 ---
            for (int i = 0; i < path.Count - 2; i++)
            {
                int dir1 = GetMoveDirection(path[i], path[i + 1]);
                int dir2 = GetMoveDirection(path[i + 1], path[i + 2]);
                if (dir1 != -1 && dir1 == dir2)
                {
                    pushCount++;
                    i++; // ★ 1手ぶんスキップ
                }
            }
        }
        return pushCount;
    }


    // --- CSV書き出し関数（★ PushCount列を追加） ---
    private void RecordResult(string ruleType, int moves, float time, int nodes, int pushCount)
    {
        // ★ルール名に「PDBあり/なし」も記録するように変更
        string ruleName = ruleType + (usePDB ? "_PDB" : "_NoPDB");

        // ★ファイル名も「PDBあり/なし」で分ける
        string fileName = usePDB ? "8puzzle_results_PDB.csv" : "8puzzle_results_NoPDB.csv";
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
                staPos[now] = new Vector2Int(i, j); // ★バグ修正済み
                count++;
            }
        }
    }

    // --- 盤面の状態(Vector2Int[])を、HashSetで使えるユニークなキー（ulong型）に変換 ---
    ulong StateToUlong(Vector2Int[] state)
    {
        int N = rowNum;
        ulong key = 0ul;
        for (int i = 0; i < state.Length && i < 9; i++)
        {
            if (i >= state.Length || state[i] == null) continue; // 安全装置
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
        if (dx > 0) return 0; // 下
        if (dx < 0) return 1; // 上
        if (dy > 0) return 2; // 右
        if (dy < 0) return 3; // 左
        return -1;
    }

    // --- 方向(dir)の逆方向を返す ---
    int GetOppositeDirection(int dir)
    {
        if (dir == 0) return 1; // 下
        if (dir == 1) return 0; // 上
        if (dir == 2) return 3; // 右
        if (dir == 3) return 2; // 左
        return -1;
    }

    // --- 「リプレイ」ボタンが押されたときの処理 ---
    public void OnButtonClickReplay()
    {
        if (solutionPath == null || solutionPath.Count == 0)
        {
            Debug.Log("再生する解がありません");
            return;
        }
        if (isReplaying)
        {
            Debug.Log("既にリプレイ中です");
            return;
        }
        StartCoroutine(ReplaySolution());
    }

    // --- 解の再生（Controllerに盤面を渡す）---
    IEnumerator PlaySolution()
    {
        Debug.Log($"解を再生します... 総手数: {solutionPath.Count - 1}");
        for (int i = 0; i < solutionPath.Count; i++)
        {
            if (Ctr.isStop) break;
            Ctr.stateTileIDA(solutionPath[i],i);
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
            Ctr.stateTileIDA(solutionPath[i],i);
            if (i < solutionPath.Count - 1)
                yield return new WaitForSeconds(0.5f);
            
        }
        if (!Ctr.isStop) { Debug.Log("リプレイ完了！"); }
        Ctr.isfinish = true; isReplaying = false;
    }

}