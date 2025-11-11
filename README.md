# SlidePuzzle-Research
このリポジトリ内のファイルを持ちいて本探究を再現する方法を以下に示す。

リポジトリの右上に出てくる「Code」をクリックし、「HTTPS」を開き、表示されているURLをコピーする。次にコマンドプロンプトをPCで開き「git clone (コピーしたURL)」と入力し実行。これによりPCに本探究のUnity Projectがダウンロードされる。次にUnity Hubを開き右上の「開く」もしくは「追加」から先ほどダウンロードしたファイルを選択しプロジェクトを開くことができるようになる。プロジェクトを開いた後は、下の方にあるProjectからAsset➡ScenesからSampleScenesをダブルクリックすることで8パズルが表示される。探究項目ごとの再現方法は、右側の「Inspector」に表示されている各種フラグや値を変更することで変更できる具体的に、

　①「Is Batch Test Mode」と「Use PDB」にチェックを入れ、Num Trialsは10000
　②　①と同じ
　③「Is Batch Test Mode」にチェックを入れ、「Use PDB」を外す。Num Trialsは1000
　④　押し込み回数は①と同じ。目視比較は、「Use PDB」にチェックを入れ、「Is Batch Test Mode」は外す。Num Trialsの値は影響しない。
　
上記のようにすることで全実験を再現することができる。実験終了後にcsvファイルがドキュメントに作成されるため、そのファイルをエクセルで開き、平均や最頻値などを調べることが可能である。

＊「Inspector」から「Is Count Interpretation」のチェックを操作することで「押し込み」操作のON OFFを切り替えられる
