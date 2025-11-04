using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using UnityEngine.UI;
using System.IO; // ★PDBのファイル保存/読み込みに必要
using System.Text; // ★CSV書き出し AND 盤面出力に必要
using System.Runtime.Serialization.Formatters.Binary; // ★PDBのシリアライズに必要
using System.Runtime.Serialization; // ★PDBのシリアライズに必要

// 8パズル専用IDA*探索スクリプト（★★ 「解の質」分析機能つき ★★）
public class IDA : MonoBehaviour
{
    // ========== 1. Unity / Controller 関連 ==========
    public Button idaButton;
    public Button replayButton;
    private Controller Ctr;

    public int numTrials = 10000; // 試行回数 (1000でもOK)

    // --- PDB（究極の攻略本）を格納する変数 ---
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
        public Vector2Int[] state; // 盤面
        public int g; // 実コスト（スタートからの手数）
        public int pathIndex; // 解の経路を復元するためのID
        public int neighborIndex; // 次に探索すべき「近傍(次の手)」のインデックス
        public List<Vector2Int[]> neighbors; // 近傍（次の手のリスト）
        public bool isReturning; // スタックから戻ってきた時かどうかのフラグ
        public int lastDir; // 戻り防止用の方向
    }

    // --- Unityの起動時に1回だけ呼ばれる ---
    void Start()
    {
        Ctr = GetComponent<Controller>();
        // ボタンが押されたら、自動テストの「親」コルーチンを起動する
        if (idaButton != null) idaButton.onClick.AddListener(OnButtonClick_StartBatchTest);

        if (replayButton != null)
        {
            replayButton.onClick.AddListener(OnButtonClickReplay);
            replayButton.interactable = false;
        }
    }

    // --- 「探索開始」ボタンが押されたときの処理 ---
    public void OnButtonClick_StartBatchTest()
    {
        if (Ctr == null) Ctr = GetComponent<Controller>();
        if (Ctr == null) { Debug.LogError("Controller が見つかりません。"); return; }
        StopAllCoroutines(); // 実行中のテストを停止
        StartCoroutine(RunBatchTest(numTrials, Ctr.isCountInterpretation));
    }


    // --- 「自動試行」の親コルーチン ---
    IEnumerator RunBatchTest(int totalTrials, bool isPushRule)
    {
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
        if (isPushRule)
        {
            Debug.Log("「押し出し」ルール専用PDBを準備します...");
            yield return StartCoroutine(BuildPatternDatabase_ForPushRule());
        }
        else // ★「標準」ルールの場合
        {
            Debug.Log("「標準」ルール専用PDBを準備します...");
            yield return StartCoroutine(BuildPatternDatabase_ForStandardRule()); // ★標準PDB用の関数を呼ぶ
        }
        if (patternDB1 == null) { Debug.LogError("PDBの準備に失敗。中止します。"); yield break; }
        Debug.Log("PDB準備が完了しました。");

        // --- 3. 1万回ループ ---
        Debug.Log("探索ループを開始します...");
        for (int i = 1; i <= totalTrials; i++)
        {
            if (Ctr.isStop) { Debug.Log("自動テストが手動で中断されました。"); yield break; }
            Ctr.Shuffle();
            ConvertFromController();
            yield return StartCoroutine(IDAStarCoroutine((Vector2Int[])staPos.Clone()));
            if (i % 1000 == 0) { Debug.Log($"--- 自動テスト進捗: {i} / {totalTrials} 回 完了 ---"); }
        }

        // --- 4. テスト完了 ---
        Debug.Log($"★★★ 自動テスト完了 ({totalTrials}回) ★★★");
        Debug.Log($"結果はPCの「ドキュメント」フォルダ内の 8puzzle_results_PDB.csv を確認してください。");
    }


    // --- IDA*探索の「親」となるコルーチン ---
    IEnumerator IDAStarCoroutine(Vector2Int[] startState)
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

                // ↓↓↓ ★★★ 「解の分析」を追加 ★★★ ↓↓↓
                int pushCount = AnalyzeSolutionPath(solutionPath, Ctr.isCountInterpretation);
                RecordResult(rule, moves, elapsed, nodesSearched, pushCount);
                // ↑↑↑ ★★★★★★★★★★★★★★★★★★★ ↑↑↑

                yield break;
            }
            if (temp == INF) { yield break; }
            threshold = temp;
        }
    }

    // --- IDA*の「本体」 (SearchIterative) ---
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

            yield return StartCoroutine(BuildSinglePatternDB_MultiSlide(
                all_tiles, patternDB1, 100
            ));

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

            yield return StartCoroutine(BuildSinglePatternDB_Standard(
                all_tiles, patternDB1, 100
            ));

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
            // (state[i] が (0,0)～(2,2) の座標)
            // (pos が 0～8 の通し番号)
            int pos = state[i].x * N + state[i].y;

            // 4bit（0～15）あれば 0～8 を表現できる
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
            // 1. ピース'i'の4bitぶんのデータを取り出す
            ulong chunk = (key >> (i * 4)) & 0xF; // 0xF は '1111'
            int pos = (int)chunk;

            // 2. 通し番号(pos: 0～8)を、(x, y) 座標に変換
            int x = pos / N;
            int y = pos % N;

            // 3. state[ピース番号] = 座標 をセット
            state[i] = new Vector2Int(x, y);
        }
        return state;
    }

    // ★ 盤面(state)を、人間が読める「3x3の文字列」にフォーマットする関数
    string FormatState(Vector2Int[] state, int N)
    {
        int[] grid = new int[N * N]; // 9マス
        // (state は [ピース番号] -> [座標] なので、[座標] -> [ピース番号] に反転させる)
        for (int piece = 0; piece < state.Length; piece++)
        {
            if (piece >= state.Length || state[piece] == null) continue; // 安全装置
            int x = state[piece].x;
            int y = state[piece].y;
            int pos = x * N + y;
            if (pos >= 0 && pos < grid.Length)
            {
                grid[pos] = piece; // (x,y)の位置に piece を置く
            }
        }

        // 3x3の文字列を組み立てる
        StringBuilder boardString = new StringBuilder();
        boardString.Append("\n--- 盤面 ---\n");
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                boardString.Append(grid[i * N + j].ToString() + " ");
            }
            boardString.Append("\n"); // 改行
        }
        return boardString.ToString();
    }

    // ★ PDBを全探索して、最長手数の盤面を全部コンソールに出力する関数
    void PrintMaxDepthBoards(Dictionary<ulong, byte> pdb, int maxDepth, string ruleName)
    {
        Debug.LogWarning($"--- 【{ruleName}ルール】最長手数 ({maxDepth}手) の盤面を探索中... ---");
        int count = 0;

        // PDB（辞書）の全181,440件を1件ずつチェック
        foreach (var pair in pdb)
        {
            // もし手数(Value)が、最長手数(maxDepth)と一致したら...
            if (pair.Value == maxDepth)
            {
                ulong key = pair.Key; // 盤面キー(ulong)

                // キーを「盤面(state)」にアンパック
                Vector2Int[] state = UnpackKeyToState(key, rowNum);

                // 盤面を「3x3の文字列」にフォーマット
                string boardText = FormatState(state, rowNum);

                // コンソールに出力！
                Debug.Log($"【{ruleName} / {maxDepth}手 盤面 {count + 1}】 {boardText}");

                count++;
            }
        }

        if (count > 0)
        {
            Debug.LogWarning($"--- {ruleName}ルールの最長({maxDepth}手)盤面は、全部で {count} 件見つかりました ---");
        }
        else
        {
            Debug.LogError("あれ？ 最長手数の盤面が見つかりませんでした。");
        }
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

    // --- PDB（攻略本）対応版ヒューリスティクス ---
    int Heuristic(Vector2Int[] pos, int lastDir)
    {
        // --- ★ 1. PDB がロード済みの場合 ★ ---
        if (patternDB1 != null)
        {
            ulong key = GetPatternKey(pos, null); // 9ピースのキーを取得
            if (patternDB1.TryGetValue(key, out byte moves))
            {
                return (int)moves; // 「答え」を返す
            }
            else { Debug.LogError($"PDBにキー {key} が見つかりません！"); }
        }

        // --- ★ 2. PDBがまだ無い（構築中）の場合 ★ ---
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
        if (patternDB1 != null)
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
        if (patternDB1 != null)
        {
            // PDBがある場合は、ソートを省略したBFS版を流用する
            return GetNeighborsMultiSlide_ForPDB(statePos);
        }

        // --- PDBが「ない」場合（＝PDB構築中）は、従来通りf値でソートする ---
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

    // --- ★ 「解の手順(solutionPath)」を分析して、「押し出し回数」をカウントする関数 ---
    private int AnalyzeSolutionPath(List<Vector2Int[]> path, bool isPushRule)
    {
        int pushCount = 0; // カウント用

        if (isPushRule)
        {
            // --- 「押し出し」ルールの場合 ---
            // (1手で2マス動いた回数を数える)
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2Int emptyA = path[i][0]; // 0番目の盤面の「空白」の位置
                Vector2Int emptyB = path[i + 1][0]; // 1番目の盤面の「空白」の位置

                // 空白の移動距離（マンハッタン距離）を計算
                int dist = Mathf.Abs(emptyA.x - emptyB.x) + Mathf.Abs(emptyA.y - emptyB.y);

                if (dist > 1) // もし移動距離が 1 より大きい（つまり 2）なら
                {
                    pushCount++; // 「本物の押し出し」としてカウント
                }
            }
        }
        else
        {
            // --- 「標準」ルールの場合 ---
            // (空白が「同じ方向」に「2連続」で動いた回数を数える)
            for (int i = 0; i < path.Count - 2; i++) // 2手先(i+2)まで見る
            {
                // A -> B の移動方向
                int dir1 = GetMoveDirection(path[i], path[i + 1]);
                // B -> C の移動方向
                int dir2 = GetMoveDirection(path[i + 1], path[i + 2]);

                if (dir1 != -1 && dir1 == dir2) // もし方向が「同じ」なら
                {
                    pushCount++; // 「実質的な押し出し」としてカウント
                    i++; // ★重要★ 1手ぶん (i+2) をスキップする
                }
            }
        }

        return pushCount;
    }


    // --- CSV書き出し関数（★ PushCount列を追加） ---
    private void RecordResult(string ruleType, int moves, float time, int nodes, int pushCount)
    {
        string filePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "/8puzzle_results_PDB.csv";
        string line = $"\"{ruleType}\",{moves},{time},{nodes},{pushCount}";
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
            Ctr.stateTileIDA(solutionPath[i]);
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
        for (int i = 0; i < solutionPath.Count; i++)
        {
            if (Ctr.isStop) { Debug.Log("リプレイが中断されました"); break; }
            Ctr.stateTileIDA(solutionPath[i]);
            if (i < solutionPath.Count - 1)
                yield return new WaitForSeconds(0.5f);
        }
        if (!Ctr.isStop) { Debug.Log("リプレイ完了！"); }
        Ctr.isfinish = true; isReplaying = false;
    }
}







//一手ずつ手順再生用のスクリプト
/*
using System.Collections;
using System.Collections.Generic;
using System.Linq; // GetNeighborsMultiSlide で .Select() を使うために必要
using System; // System.Action や Mathf.Ceil のために必要
using UnityEngine;
using UnityEngine.UI;

// 8パズル専用のIDA*探索スクリプト
public class IDA : MonoBehaviour
{
    // ========== 1. Unity / Controller 関連 ==========

    public Button idaButton; // UnityのUIボタン（探索開始）
    public Button replayButton; // UnityのUIボタン（リプレイ）
    private Controller Ctr; // パズルの本体(Controller.cs)への参照

    // 内部で使うパズルの状態
    private int rowNum; // パズルの行数（8パズルなので 3 になる）
    private Vector2Int[] nowPos; // ピース番号→現在座標
    private Vector2Int[] finPos; // ピース番号→ゴール座標
    private Vector2Int[] staPos; // ピース番号→スタート座標

    // 探索の管理用
    private List<Vector2Int[]> solutionPath = null; // 解の手順（盤面のリスト）
    private const int INF = int.MaxValue; // 無限大の代わり
    private int nodesSearched = 0; // 探索したノード（盤面）の総数
    private float startSearch = 0f; // 探索の開始時刻

    // リプレイ用
    private bool isReplaying = false;

    // ヒューリスティクス（マンハッタン距離）の事前計算テーブル
    // [ピース番号, マスの通し番号] = 距離
    private int[,] manhattanTable = null;


    // ========== 2. IDA*アルゴリズムの本体 ==========

    // IDA*の探索で使う「ノード（状態）」をスタックに積むためのクラス
    private class SearchNode
    {
        public Vector2Int[] state; // 盤面
        public int g; // 実コスト（スタートからの手数）
        public int pathIndex; // 解の経路を復元するためのID
        public int neighborIndex; // 次に探索すべき「近傍(次の手)」のインデックス
        public List<Vector2Int[]> neighbors; // 近傍（次の手のリスト）
        public bool isReturning; // スタックから戻ってきた時かどうかのフラグ
        public int lastDir; // 戻り防止用の方向 (0:下, 1:上, 2:右, 3:左)
    }

    // --- Unityの起動時に1回だけ呼ばれる ---
    void Start()
    {
        // Controller.cs を取得
        Ctr = GetComponent<Controller>();

        // ボタンがクリックされたら、OnButtonClickStart 関数を呼ぶように設定
        if (idaButton != null) idaButton.onClick.AddListener(OnButtonClickStart);

        // リプレイボタンの設定
        if (replayButton != null)
        {
            replayButton.onClick.AddListener(OnButtonClickReplay);
            replayButton.interactable = false; //最初は押せない
        }
    }

    // --- 「探索開始」ボタンが押されたときの処理 ---
    public void OnButtonClickStart()
    {
        if (isReplaying) return; // リプレイ中は実行しない

        if (Ctr == null) Ctr = GetComponent<Controller>();
        if (Ctr == null)
        {
            Debug.LogError("Controller が見つかりません。");
            return;
        }

        // Controllerから現在のパズルの情報を取得
        // (8パズルの場合、Ctr.rowNum は 3, Ctr.tileNum は 8 が入るはず)
        rowNum = Ctr.rowNum;

        // 配列を初期化（[0]～[8] までの 9要素）
        nowPos = new Vector2Int[Ctr.tileNum + 1];
        finPos = new Vector2Int[Ctr.tileNum + 1];
        staPos = new Vector2Int[Ctr.tileNum + 1];

        // Controllerのデータ形式からIDA*用の形式に変換
        ConvertFromController();

        // 探索用の変数をリセット
        Ctr.isStop = false; // 中断フラグをリセット
        solutionPath = null; // 前回の解をリセット
        if (replayButton != null) replayButton.interactable = false;

        // 既に動いている探索（コルーチン）があれば停止する
        StopAllCoroutines();

        // 8パズルなので、PDBの構築などは「不要」
        Debug.Log("8パズルモード: IDA*アルゴリズムで探索を開始します。");

        // IDA*の探索コルーチンを開始
        // staPos (スタート時の盤面) を渡す
        StartCoroutine(IDAStarCoroutine((Vector2Int[])staPos.Clone()));
    }
    // --- 「リプレイ」ボタンが押されたときの処理 ---
    // (Start()関数で replayButton.onClick に登録されている)
    public void OnButtonClickReplay()
    {
        // 既に再生する解（solutionPath）がなければ何もしない
        if (solutionPath == null || solutionPath.Count == 0)
        {
            Debug.Log("再生する解がありません");
            return;
        }

        // 既にリプレイ中なら何もしない
        if (isReplaying)
        {
            Debug.Log("既にリプレイ中です");
            return;
        }

        // ReplaySolutionコルーチン（IEnumerator）を開始する
        StartCoroutine(ReplaySolution());
    }

    // ========== 2. IDA*アルゴリズムの本体 ==========
    // (↑この関数の下に、IDAStarCoroutine などが続く...)

    // --- IDA*探索の「親」となるコルーチン ---
    // (threshold（閾値）を上げながら、SearchIterativeを繰り返し呼ぶ)
    IEnumerator IDAStarCoroutine(Vector2Int[] startState)
    {
        // 1. マンハッタン距離の早見表を（念のため）構築
        BuildManhattanTable();

        solutionPath = null; // 解をリセット

        // 2. 最初の「閾値(threshold)」を、スタート地点のヒューリスティクス値に設定
        //    (この時点で Heuristic 関数がルール(isCountInterpretation)を判別する)
        int threshold = Heuristic(startState, -1);

        startSearch = Time.realtimeSinceStartup; // 時間計測開始
        Debug.Log($"IDA*探索開始 初期閾値={threshold} (ルール: {(Ctr.isCountInterpretation ? "押し出し" : "標準")})");

        int iteration = 0; // 反復回数

        // 3. 解が見つかるか、「解なし」(temp == INF) が確定するまで無限ループ
        while (true)
        {
            if (Ctr.isStop) { Debug.Log("探索が中断されました"); yield break; } // 中断処理

            iteration++;
            nodesSearched = 0; // 探索ノード数をリセット

            int temp = INF; // 次の反復で使う「新しい閾値」を保持する変数

            // 4. ★ 閾値(threshold) を使って、深さ優先探索(DFS)を1回実行
            // (SearchIterative は「閾値(threshold) を超えたf値の最小値」を返す)
            yield return StartCoroutine(SearchIterative(startState, threshold, -1, result => temp = result));

            float elapsed = Time.realtimeSinceStartup - startSearch;

            if (Ctr.isStop) { Debug.Log("探索が中断されました"); yield break; }

            // 5. ★ 解が見つかった場合 (solutionPathに中身が入った)
            if (solutionPath != null)
            {
                Debug.Log($"解発見! 手数={solutionPath.Count - 1}, 探索時間={elapsed:F2}秒, ノード数={nodesSearched}, 反復回数={iteration}");
                if (replayButton != null) replayButton.interactable = true;
                yield return StartCoroutine(PlaySolution()); // 解を再生
                yield break;
            }

            // 6. ★ 閾値を超えるf値が見つからなかった場合（＝探索し尽くしたけど解がなかった）
            if (temp == INF)
            {
                Debug.Log($"解なし（探索時間={elapsed:F2}秒）");
                yield break;
            }

            // 7. ★ 解が見つからなかったので、閾値を更新して次の反復へ
            threshold = temp; // 閾値を、見つかった「次のf値の最小値」に更新
            Debug.Log($"反復{iteration}: 閾値={threshold}, ノード数={nodesSearched}, 経過時間={elapsed:F2}秒");
        }
    }

    // --- IDA*の「本体」。スタックを使った反復深化DFS ---
    // (System.Action<int> callback は、コルーチンから結果(temp)を受け取るための仕組み)
    IEnumerator SearchIterative(Vector2Int[] startState, int threshold, int prevDir, System.Action<int> callback)
    {
        var stack = new Stack<SearchNode>(); // DFSのためのスタック
        var visited = new HashSet<ulong>(); // 経路上のループ防止用
        int minNextThreshold = INF; // 次の反復で使う「閾値」の候補

        // 経路復元用の辞書（キー:ID, 値:そのIDまでの盤面リスト）
        var nodePaths = new Dictionary<int, List<Vector2Int[]>>();
        int nodeIdCounter = 0;

        // スタート地点の「経路」を作成
        var initialPath = new List<Vector2Int[]> { (Vector2Int[])startState.Clone() };
        nodePaths[nodeIdCounter] = initialPath; // ID 0 番にスタート盤面を登録

        // スタートノードをスタックに積む
        stack.Push(new SearchNode
        {
            state = (Vector2Int[])startState.Clone(),
            g = 0, // コスト0
            pathIndex = nodeIdCounter, // 経路ID 0
            neighborIndex = 0, // まだ近傍を調べてない
            neighbors = null,
            isReturning = false,
            lastDir = prevDir
        });
        nodeIdCounter++;


        int operationCount = 0;
        float lastLogTime = Time.realtimeSinceStartup;

        // スタックが空になるまで（＝今回の閾値での探索が終わるまで）ループ
        while (stack.Count > 0)
        {
            if (Ctr.isStop) { callback(INF); yield break; } // 中断

            operationCount++;

            // Unityが固まらないよう、定期的に処理をOSに返す (yield)
            // 10000回に1回
            if (operationCount % 10000 == 0)
            {
                yield return null;
            }

            var node = stack.Pop(); // スタックからノード（盤面）を取り出す

            // --- 帰りの処理（スタックに戻ってきたときの処理） ---
            if (node.isReturning)
            {
                // このノードの経路情報はもう不要なので削除
                if (nodePaths.ContainsKey(node.pathIndex))
                    nodePaths.Remove(node.pathIndex);
                // 訪問リストからも削除（別ルートでここに来れるように）
                visited.Remove(StateToUlong(node.state));
                continue; // 次のループへ
            }

            // --- 行きの処理（ノードを評価する） ---
            if (node.neighborIndex == 0) // このノードに初めて到達した時
            {
                nodesSearched++;
                ulong key = StateToUlong(node.state); // 盤面をハッシュキーに変換

                if (visited.Contains(key)) // 既に訪問済み（ループ）ならスキップ
                {
                    continue;
                }
                visited.Add(key); // 訪問リストに追加

                // ★★★ IDA*の核心 (1) ★★★
                int h = Heuristic(node.state, node.lastDir); // 1. ヒューリスティクスを計算
                int f = node.g + h; // 2. 評価値 f = g + h を計算

                // 3. 評価値が「閾値」を超えているか？
                if (f > threshold)
                {
                    visited.Remove(key); // 訪問リストから削除（別経路で来れるように）

                    // 「閾値を超えたf値」のうち、最小のものを記録しておく
                    // これが次の反復(iteration)の「新しい閾値」になる
                    if (f < minNextThreshold)
                        minNextThreshold = f;

                    continue; // ★ この枝はこれ以上探索しない（枝刈り）
                }

                // ★ ゴール判定 ★
                if (h == 0 && SameState(node.state, finPos))
                {
                    // 解発見！
                    solutionPath = new List<Vector2Int[]>();
                    var currentPath = nodePaths[node.pathIndex]; // 経路IDから経路リストを取得
                    foreach (var p in currentPath)
                        solutionPath.Add((Vector2Int[])p.Clone()); // 解のパスをコピー

                    callback(node.g); // 探索終了（コールバックで手数を返す）
                    yield break;
                }

                // --- ★★★ あなたの研究の核心 ★★★ ---
                // ルール(isCountInterpretation)に応じて、「次の手」の生成方法を切り替える
                if (Ctr.isCountInterpretation)
                {
                    // 「押し出し」ルールで次の手のリストを取得
                    node.neighbors = GetNeighborsMultiSlide(node.state, node.g, node.lastDir);
                }
                else
                {
                    // 通常の「1マス移動」ルールで次の手のリストを取得
                    node.neighbors = GetNeighbors1WithPruning(node.state, node.lastDir);
                }
                // (f値順にソートされてリストが返ってくる)
            }

            // --- 近傍（次の手）をスタックに積む処理 ---
            if (node.neighborIndex < node.neighbors.Count) // まだ試していない「次の手」があるか？
            {
                // 次に処理する近傍(nextState)を取得
                var nextState = node.neighbors[node.neighborIndex];
                node.neighborIndex++; // インデックスを+1

                int moveDir = GetMoveDirection(node.state, nextState);

                // --- 経路の複製 ---
                var currentPath = nodePaths[node.pathIndex]; // 親の経路を取得
                var newPath = new List<Vector2Int[]>(currentPath); // 親の経路をコピー
                newPath.Add((Vector2Int[])nextState.Clone()); // 新しい盤面を追加
                int newNodeId = nodeIdCounter++; // 新しい経路ID
                nodePaths[newNodeId] = newPath; // 新しい経路を辞書に登録
                // ---

                // (DFSのためのスタック操作)
                // 1. 親ノードをスタックに戻す（戻ってきたときに他の近傍を処理するため）
                stack.Push(node);

                // 2. 帰りの処理（isReturning=true）をするためのノードを積む
                stack.Push(new SearchNode
                {
                    state = nextState,
                    g = node.g + 1,
                    pathIndex = newNodeId,
                    neighborIndex = 0,
                    neighbors = null,
                    isReturning = true,
                    lastDir = moveDir
                });

                // 3. 行きの処理（isReturning=false）をするためのノードを積む（＝次にPopされる）
                stack.Push(new SearchNode
                {
                    state = nextState,
                    g = node.g + 1, // ★ コストを+1（「押し出し」でも+1）
                    pathIndex = newNodeId,
                    neighborIndex = 0,
                    neighbors = null,
                    isReturning = false,
                    lastDir = moveDir
                });
            }
            else // このノードのすべての近傍を探索し終わった
            {
                if (nodePaths.ContainsKey(node.pathIndex))
                    nodePaths.Remove(node.pathIndex); // 経路情報を削除
                visited.Remove(StateToUlong(node.state)); // 訪問リストから削除
            }
        }

        // スタックが空になった（＝今回の閾値での探索がすべて終わった）
        callback(minNextThreshold); // 次の閾値候補を返す
    }


    // ========== 3. ヒューリスティクス & 次の手の生成 ==========

    // --- マンハッタン距離の「早見表」を作る関数 ---
    void BuildManhattanTable()
    {
        if (manhattanTable != null) return; // 既に作ってあれば何もしない

        int N = rowNum;
        int tiles = Ctr.tileNum; // 8
        manhattanTable = new int[tiles + 1, N * N]; // [9, 9] の2次元配列

        for (int t = 1; t <= tiles; t++) // ピース1から8まで
        {
            int gx = finPos[t].x; // ピースtのゴールX座標
            int gy = finPos[t].y; // ピースtのゴールY座標

            for (int px = 0; px < N; px++) // 盤面の全マス(px, py)について
                for (int py = 0; py < N; py++)
                {
                    // ピースtが(px, py)にある時のマンハッタン距離を計算し、テーブルに保存
                    manhattanTable[t, px * N + py] = Mathf.Abs(gx - px) + Mathf.Abs(gy - py);
                }
        }
    }

    // ★★★★★★ あなたの研究で最重要の関数 (1) ★★★★★★
    // ヒューリスティクス（ゴールまでの予測コスト）を計算する
    int Heuristic(Vector2Int[] pos, int lastDir)
    {
        int N = rowNum;
        int h_val = 0; // ヒューリスティクス値（マンハッタン距離の合計）

        // 8パズル: マンハッタン距離(Manhattan) + 線形コンフリクト(Linear Conflict)

        // 1. 全ピースのマンハッタン距離の合計
        for (int t = 1; t <= Ctr.tileNum; t++)
        {
            // 現在位置(pos[t])から、事前計算テーブル(manhattanTable)を使って距離を引いてくる
            int idx = pos[t].x * N + pos[t].y;
            h_val += manhattanTable[t, idx];
        }

        // 2. 線形コンフリクトのコストを追加
        h_val += LinearConflict(pos);

        // --- ★★★ ここからが「押し出し」ルール対応 ★★★ ---

        // もし「押し出し」ルール（isCountInterpretation）が有効なら、
        // 計算したヒューリスティクス値(h_val)を「1手で動かせる最大ピース数(k)」で割る
        if (Ctr.isCountInterpretation)
        {
            // 8パズル(N=3)なので、k = 2
            int maxSlide = (N == 4) ? 3 : 2;

            if (maxSlide > 0 && h_val > 0)
            {
                // (h_val / 2) の「切り上げ」を計算して返す
                // これが「押し出し」ルールにおける許容的なヒューリスティクス h'(n) となる
                return (int)Mathf.Ceil((float)h_val / (float)maxSlide);
            }
            else
            {
                return h_val; // h_valが0の場合はそのまま0を返す
            }
        }
        else
        {
            // 通常ルールの場合は、計算した h_val をそのまま返す
            return h_val;
        }
    }

    // --- 線形コンフリクト(Linear Conflict)を計算する ---
    // (ゴールと同じ行/列にあるピースが、ゴールと逆順なら+2手)
    int LinearConflict(Vector2Int[] pos)
    {
        int add = 0; // 追加コスト
        int N = rowNum;

        // 1. 行(Row)のコンフリクトをチェック
        for (int r = 0; r < N; r++)
        {
            var rowTiles = new List<int>();
            // (r行目) にあり、かつゴールも(r行目)にあるピースをリストアップ
            for (int t = 1; t <= Ctr.tileNum; t++)
                if (pos[t].x == r && finPos[t].x == r)
                    rowTiles.Add(t);

            // リストアップしたピース同士で比較
            for (int a = 0; a < rowTiles.Count; a++)
                for (int b = a + 1; b < rowTiles.Count; b++)
                {
                    int ta = rowTiles[a];
                    int tb = rowTiles[b];
                    // taがtbより左(y小)にあるのに、ゴールでは右(y大)にある場合
                    if (pos[ta].y < pos[tb].y && finPos[ta].y > finPos[tb].y)
                        add += 2; // コンフリクト！ +2手
                }
        }

        // 2. 列(Column)のコンフリクトをチェック（上と同様）
        for (int c = 0; c < N; c++)
        {
            var colTiles = new List<int>();
            for (int t = 1; t <= Ctr.tileNum; t++)
                if (pos[t].y == c && finPos[t].y == c)
                    colTiles.Add(t);

            for (int a = 0; a < colTiles.Count; a++)
                for (int b = a + 1; b < colTiles.Count; b++)
                {
                    int ta = colTiles[a];
                    int tb = colTiles[b];
                    if (pos[ta].x < pos[tb].x && finPos[ta].x > finPos[tb].x)
                        add += 2;
                }
        }
        return add;
    }

    // --- 「標準ルール」：空白の1マス移動（戻り防止あり） ---
    List<Vector2Int[]> GetNeighbors1WithPruning(Vector2Int[] statePos, int lastDir)
    {
        var list = new List<Vector2Int[]>();
        int N = rowNum;
        Vector2Int empty = statePos[0]; // 空白の現在位置
        int[] dx = { 1, -1, 0, 0 }; // 下, 上
        int[] dy = { 0, 0, 1, -1 }; // 右, 左

        int oppositeDir = GetOppositeDirection(lastDir); // 逆方向

        for (int dir = 0; dir < 4; dir++)
        {
            // ★ もし今から動かす方向(dir)が、直前に来た方向(oppositeDir)ならスキップ
            if (dir == oppositeDir) continue;

            int nx = empty.x + dx[dir]; // 空白の移動先X
            int ny = empty.y + dy[dir]; // 空白の移動先Y

            // 盤外ならスキップ
            if (nx < 0 || nx >= N || ny < 0 || ny >= N) continue;

            // (nx, ny) にいるピース番号(tileNum)を探す
            int tileNum = -1;
            for (int t = 1; t <= Ctr.tileNum; t++)
            {
                if (statePos[t].x == nx && statePos[t].y == ny)
                {
                    tileNum = t;
                    break;
                }
            }
            if (tileNum < 0) continue; // (基本ありえないが)

            // 新しい盤面(ns)を作成
            Vector2Int[] ns = (Vector2Int[])statePos.Clone();
            // 空白(0)とピース(tileNum)の位置を交換
            Vector2Int tmp = ns[0];
            ns[0] = ns[tileNum]; // 空白の位置 = ピースの元位置
            ns[tileNum] = tmp; // ピースの位置 = 空白の元位置

            list.Add(ns);
        }

        // IDA*はf値でソートすると効率が上がる...が、
        // 8パズル程度ならソートしなくても十分速いので、リストをそのまま返す
        return list;
    }

    // ★★★★★★ あなたの研究で最重要の関数 (2) ★★★★★★
    // 「押し出し」ルール：1回のプッシュ（コスト1）で複数枚を動かす
    List<Vector2Int[]> GetNeighborsMultiSlide(Vector2Int[] statePos, int g, int lastDir)
    {
        //「次の手」の候補を (盤面, f値) のペアで持つリスト
        var candidates = new List<(Vector2Int[], int)>();
        int N = rowNum;
        // ★ 8パズル(3x3)なので、最大2枚まで同時に押せる
        int maxSlide = (N == 4) ? 3 : 2;

        Vector2Int empty = statePos[0]; // 空白の位置
        int[] dx = { 1, -1, 0, 0 }; // 下, 上
        int[] dy = { 0, 0, 1, -1 }; // 右, 左

        int oppositeDir = GetOppositeDirection(lastDir); // 戻り防止

        // 4方向（上下左右）について
        for (int dir = 0; dir < 4; dir++)
        {
            if (dir == oppositeDir) continue; // 戻り防止

            // ★ 1枚押し(slideCount=1)、2枚押し(slideCount=2) をすべて試す
            for (int slideCount = 1; slideCount <= maxSlide; slideCount++)
            {
                bool ok = true;
                var tiles = new List<int>(); // 押されて動くピースのリスト

                // 1枚目からslideCount枚目までチェック
                for (int s = 1; s <= slideCount; s++)
                {
                    int nx = empty.x + dx[dir] * s; // 空白からdir方向にsマス目
                    int ny = empty.y + dy[dir] * s;

                    if (nx < 0 || nx >= N || ny < 0 || ny >= N) // 盤外ならNG
                    {
                        ok = false;
                        break;
                    }

                    // (nx, ny) にあるピース番号(tileNum)を探す
                    int tileNum = -1;
                    for (int t = 1; t <= Ctr.tileNum; t++)
                    {
                        if (statePos[t].x == nx && statePos[t].y == ny)
                        {
                            tileNum = t;
                            break;
                        }
                    }

                    if (tileNum <= 0) // ピースが見つからなかったらNG
                    {
                        ok = false;
                        break;
                    }
                    tiles.Add(tileNum); // 動くピースとしてリストに追加
                }

                if (!ok) continue; // この枚数・方向のプッシュは実行不可

                // --- 実際にピースを動かした「次の盤面(ns)」を生成 ---
                Vector2Int[] orig = (Vector2Int[])statePos.Clone();
                Vector2Int[] ns = (Vector2Int[])statePos.Clone();

                int k = tiles.Count; // k = slideCount

                // (例) 0 1 2 -> 1 2 0 (k=2 の場合)
                // 空白(0)の位置は、最後に押されたピース(tiles[k-1])の位置になる
                ns[0] = orig[tiles[k - 1]];

                // 2番目に押されたピース(tiles[i])は、1番目(tiles[i-1])の位置に動く
                for (int i = k - 1; i >= 1; i--)
                {
                    ns[tiles[i]] = orig[tiles[i - 1]];
                }

                // 最初に押されたピース(tiles[0])は、元の空白(orig[0])の位置に動く
                ns[tiles[0]] = orig[0];
                // --------------------------------------------------

                // ★ IDA*の効率化：f = g + 1 + h を計算
                // (g+1 は「次の手」のコスト。hは「次の盤面」の予測コスト)
                int h = Heuristic(ns, dir);
                int f = g + 1 + h;
                candidates.Add((ns, f)); // (盤面, f値) のペアでリストに追加
            }
        }

        // f値が低い（有望そうな）順にソート
        candidates.Sort((a, b) => a.Item2.CompareTo(b.Item2));

        // ソートした結果の「盤面リスト」だけを返す
        return candidates.Select(c => c.Item1).ToList();
    }


    // ========== 4. 補助関数 & リプレイ ==========

    // --- Controllerのデータ形式(1次元配列)からIDA*形式(ピース→座標)に変換 ---
    void ConvertFromController()
    {
        int count = 0;
        for (int i = 0; i < rowNum; i++)
        {
            for (int j = 0; j < rowNum; j++)
            {
                // Ctr.nowState[0] = (0,0)マスにあるピース番号
                int now = Ctr.nowState[count];
                int fin = Ctr.finishState[count];
                int sta = Ctr.startState[count];

                // ピース番号をインデックスにして、座標を格納
                nowPos[now] = new Vector2Int(i, j); // nowPos[ピース5] = (0,0)
                finPos[fin] = new Vector2Int(i, j); // finPos[ピース1] = (0,0)
                staPos[sta] = new Vector2Int(i, j);
                count++;
            }
        }
    }

    // --- 盤面の状態(Vector2Int[])を、HashSetで使えるユニークなキー（ulong型）に変換 ---
    ulong StateToUlong(Vector2Int[] state)
    {
        int N = rowNum;
        ulong key = 0ul;
        // 0(空白)～8(ピース) の9個のピースの位置をパックする
        for (int i = 0; i < state.Length && i < 9; i++)
        {
            // 座標(x,y)を (0～8) の通し番号に
            int posIndex = state[i].x * N + state[i].y;

            // 4bit あれば 0～8 を表現できる
            ulong v = (ulong)(posIndex & 0xF);

            // i*4 ビット左にずらして合成
            key |= (v << (i * 4));
        }
        return key;
    }

    // --- 2つの盤面が（ピースの配置が）完全に一致するかどうかを判定 ---
    bool SameState(Vector2Int[] a, Vector2Int[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) // 0(空白)から全ピースについて
            if (a[i] != b[i]) return false; // 座標が違ったらNG
        return true;
    }

    // --- 空白がどの方向に動いたかを 0-3 の数値で返す ---
    int GetMoveDirection(Vector2Int[] prevState, Vector2Int[] nextState)
    {
        Vector2Int prevEmpty = prevState[0];
        Vector2Int nextEmpty = nextState[0];
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


    // --- 解の再生（Controllerに盤面を渡す）---
    IEnumerator PlaySolution()
    {
        Debug.Log($"解を再生します... 総手数: {solutionPath.Count - 1}");
        for (int i = 0; i < solutionPath.Count; i++)
        {
            if (Ctr.isStop) break;

            // Controller.cs の stateTileIDA 関数を呼んで、盤面を更新
            Ctr.stateTileIDA(solutionPath[i]);
            if (i == 0) Ctr.clickCount = 0;
            if (i < solutionPath.Count - 1)
                yield return new WaitForSeconds(0.5f); // 0.5秒待つ
        }
        if (!Ctr.isStop) { Ctr.finCount = 2; Debug.Log("解の再生完了！"); }
    }

    // --- リプレイの再生 ---
    IEnumerator ReplaySolution()
    {
        Ctr.isStart = true; Ctr.isfinish = false; isReplaying = true; Ctr.clickCount = 0;
        Debug.Log($"リプレイを開始します... 総手数: {solutionPath.Count - 1}");
        for (int i = 0; i < solutionPath.Count; i++)
        {
            if (Ctr.isStop) { Debug.Log("リプレイが中断されました"); break; }

            Ctr.stateTileIDA(solutionPath[i]);
            if (i == 0) Ctr.clickCount = 0;
            if (i < solutionPath.Count - 1)
                yield return new WaitForSeconds(0.5f);
        }
        if (!Ctr.isStop) { Debug.Log("リプレイ完了！"); }
        Ctr.isfinish = true; isReplaying = false;
    }
}
*/
