namespace Tempo;

// Columns: English | es | fr | de | pt | it | ja | zh | ko      (English text is the lookup key)
public static partial class Loc
{
    static readonly string[] RowsF =
    {
        // ---- command palette ----
        "Search or jump to…|Buscar o ir a…|Rechercher ou aller à…|Suchen oder springen zu…|Pesquisar ou ir para…|Cerca o vai a…|検索または移動…|搜索或跳转到…|검색 또는 이동…",
        "Search…|Buscar…|Rechercher…|Suchen…|Pesquisar…|Cerca…|検索…|搜索…|검색…",
        "Search|Buscar|Rechercher|Suchen|Pesquisar|Cerca|検索|搜索|검색",
        "Go to {0}|Ir a {0}|Aller à {0}|Gehe zu {0}|Ir para {0}|Vai a {0}|{0} を開く|前往{0}|{0}(으)로 이동",
        "Switch timer to {0}|Cambiar el temporizador a {0}|Basculer le minuteur sur {0}|Timer zu {0} wechseln|Mudar o cronômetro para {0}|Passa il timer a {0}|タイマーを {0} に切り替え|将计时器切换到 {0}|타이머를 {0}(으)로 전환",
        "Start timer: {0}|Iniciar temporizador: {0}|Démarrer le minuteur : {0}|Timer starten: {0}|Iniciar cronômetro: {0}|Avvia timer: {0}|タイマー開始: {0}|开始计时:{0}|타이머 시작: {0}",
        "New invoice|Nueva factura|Nouvelle facture|Neue Rechnung|Nova fatura|Nuova fattura|新しい請求書|新建发票|새 청구서",
        "New quote|Nuevo presupuesto|Nouveau devis|Neues Angebot|Novo orçamento|Nuovo preventivo|新しい見積書|新建报价单|새 견적서",
        "No matches|Sin resultados|Aucun résultat|Keine Treffer|Nenhum resultado|Nessun risultato|一致するものはありません|没有匹配项|일치하는 항목 없음",

        // ---- reminders ----
        "You're not tracking time. Want to start the timer?|No estás registrando tiempo. ¿Quieres iniciar el temporizador?|Vous ne suivez pas votre temps. Démarrer le minuteur ?|Du erfasst gerade keine Zeit. Timer starten?|Você não está registrando tempo. Quer iniciar o cronômetro?|Non stai registrando il tempo. Vuoi avviare il timer?|時間を記録していません。タイマーを開始しますか?|你没有在计时。要开始计时吗?|시간을 기록하고 있지 않습니다. 타이머를 시작할까요?",
        "Your timer is still running. Did you forget to stop it?|Tu temporizador sigue en marcha. ¿Olvidaste detenerlo?|Votre minuteur tourne toujours. Avez-vous oublié de l'arrêter ?|Dein Timer läuft noch. Vergessen zu stoppen?|Seu cronômetro ainda está rodando. Esqueceu de parar?|Il timer è ancora in funzione. Hai dimenticato di fermarlo?|タイマーがまだ動いています。止め忘れていませんか?|计时器仍在运行。忘记停止了吗?|타이머가 아직 실행 중입니다. 멈추는 것을 잊으셨나요?",
        "That shortcut is already used by another app.|Otra aplicación ya usa ese atajo.|Ce raccourci est déjà utilisé par une autre application.|Dieses Kürzel wird bereits von einer anderen App verwendet.|Esse atalho já é usado por outro app.|Questa scorciatoia è già usata da un'altra app.|このショートカットは別のアプリで使用されています。|该快捷键已被其他应用占用。|이 단축키는 이미 다른 앱에서 사용 중입니다.",

        // ---- settings: shortcuts & reminders ----
        "Shortcuts & reminders|Atajos y recordatorios|Raccourcis et rappels|Kürzel & Erinnerungen|Atalhos e lembretes|Scorciatoie e promemoria|ショートカットとリマインダー|快捷键与提醒|단축키 및 알림",
        "So you never lose billable time to a forgotten timer.|Para no perder nunca tiempo facturable por un temporizador olvidado.|Pour ne jamais perdre de temps facturable à cause d'un minuteur oublié.|Damit du nie abrechenbare Zeit durch einen vergessenen Timer verlierst.|Para nunca perder tempo faturável por um cronômetro esquecido.|Per non perdere mai tempo fatturabile per un timer dimenticato.|タイマーの付け忘れで請求できる時間を失わないように。|再也不会因忘记计时而损失可计费时间。|타이머를 잊어 청구 가능한 시간을 놓치지 않도록.",
        "GLOBAL SHORTCUT|ATAJO GLOBAL|RACCOURCI GLOBAL|GLOBALES KÜRZEL|ATALHO GLOBAL|SCORCIATOIA GLOBALE|グローバルショートカット|全局快捷键|전역 단축키",
        "REMIND ME TO TRACK|RECORDARME REGISTRAR|ME RAPPELER DE SUIVRE|ERINNERN ZU ERFASSEN|LEMBRAR DE REGISTRAR|RICORDAMI DI REGISTRARE|記録のリマインダー|提醒我计时|기록 알림",
        "END-OF-DAY CHECK|AVISO DE FIN DE JORNADA|VÉRIFICATION DE FIN DE JOURNÉE|FEIERABEND-CHECK|AVISO DE FIM DO DIA|CONTROLLO DI FINE GIORNATA|終業時のチェック|下班检查|퇴근 시간 확인",
        "Every 15 minutes|Cada 15 minutos|Toutes les 15 minutes|Alle 15 Minuten|A cada 15 minutos|Ogni 15 minuti|15分ごと|每 15 分钟|15분마다",
        "Every 30 minutes|Cada 30 minutos|Toutes les 30 minutes|Alle 30 Minuten|A cada 30 minutos|Ogni 30 minuti|30分ごと|每 30 分钟|30분마다",
        "Every 60 minutes|Cada 60 minutos|Toutes les 60 minutes|Alle 60 Minuten|A cada 60 minutos|Ogni 60 minuti|60分ごと|每 60 分钟|60분마다",
        "e.g. 18:00|p. ej. 18:00|ex. 18:00|z. B. 18:00|ex.: 18:00|es. 18:00|例: 18:00|例如 18:00|예: 18:00",
        "Use a time like 18:00.|Usa una hora como 18:00.|Utilisez une heure comme 18:00.|Gib eine Uhrzeit wie 18:00 ein.|Use um horário como 18:00.|Usa un orario come 18:00.|18:00 のように入力してください。|请输入类似 18:00 的时间。|18:00 형식으로 입력하세요.",
        "Start Tempo when I sign in to Windows (in the tray)|Iniciar Tempo al entrar en Windows (en la bandeja)|Lancer Tempo à l'ouverture de session Windows (dans la zone de notification)|Tempo bei der Windows-Anmeldung starten (im Infobereich)|Iniciar o Tempo ao entrar no Windows (na bandeja)|Avvia Tempo all'accesso a Windows (nell'area di notifica)|Windows サインイン時に Tempo を起動 (通知領域)|登录 Windows 时启动 Tempo(在托盘中)|Windows 로그인 시 Tempo 시작 (알림 영역)",
        "Windows didn't allow that change.|Windows no permitió ese cambio.|Windows n'a pas autorisé ce changement.|Windows hat diese Änderung nicht erlaubt.|O Windows não permitiu essa alteração.|Windows non ha consentito questa modifica.|Windows がこの変更を許可しませんでした。|Windows 不允许此更改。|Windows에서 이 변경을 허용하지 않았습니다.",
        "The global shortcut starts or pauses the timer from any app. The end-of-day check warns you once if a timer is still running at that time.|El atajo global inicia o pausa el temporizador desde cualquier aplicación. El aviso de fin de jornada te avisa una vez si un temporizador sigue en marcha a esa hora.|Le raccourci global démarre ou met en pause le minuteur depuis n'importe quelle application. La vérification de fin de journée vous prévient une fois si un minuteur tourne encore à cette heure.|Das globale Kürzel startet oder pausiert den Timer aus jeder App. Der Feierabend-Check warnt dich einmal, wenn um diese Uhrzeit noch ein Timer läuft.|O atalho global inicia ou pausa o cronômetro em qualquer app. O aviso de fim do dia alerta uma vez se um cronômetro ainda estiver rodando nesse horário.|La scorciatoia globale avvia o mette in pausa il timer da qualsiasi app. Il controllo di fine giornata ti avvisa una volta se a quell'ora un timer è ancora attivo.|グローバルショートカットは、どのアプリからでもタイマーを開始・一時停止します。終業時のチェックは、その時刻にタイマーが動いていれば一度だけ知らせます。|全局快捷键可在任何应用中开始或暂停计时。下班检查会在该时间计时器仍在运行时提醒你一次。|전역 단축키로 어떤 앱에서든 타이머를 시작하거나 일시 정지합니다. 퇴근 시간 확인은 그 시각에 타이머가 실행 중이면 한 번 알려 줍니다.",
        "Start / pause from any app|Iniciar / pausar desde cualquier aplicación|Démarrer / pause depuis n'importe quelle application|Starten / pausieren aus jeder App|Iniciar / pausar em qualquer app|Avvia / pausa da qualsiasi app|どのアプリからでも開始 / 一時停止|在任何应用中开始 / 暂停|어떤 앱에서든 시작 / 일시 정지",

        // ---- settings: automatic backups ----
        "Automatic backups|Copias automáticas|Sauvegardes automatiques|Automatische Sicherungen|Backups automáticos|Backup automatici|自動バックアップ|自动备份|자동 백업",
        "Once a day, Tempo copies your data into a folder you choose, such as a OneDrive or Dropbox folder.|Una vez al día, Tempo copia tus datos en la carpeta que elijas, por ejemplo una de OneDrive o Dropbox.|Une fois par jour, Tempo copie vos données dans le dossier de votre choix, par exemple un dossier OneDrive ou Dropbox.|Einmal am Tag kopiert Tempo deine Daten in einen Ordner deiner Wahl, z. B. einen OneDrive- oder Dropbox-Ordner.|Uma vez por dia, o Tempo copia seus dados para uma pasta escolhida, como uma pasta do OneDrive ou Dropbox.|Una volta al giorno Tempo copia i tuoi dati in una cartella a scelta, ad esempio di OneDrive o Dropbox.|1日1回、選んだフォルダー (OneDrive や Dropbox など) にデータをコピーします。|Tempo 每天一次将数据复制到你选择的文件夹,例如 OneDrive 或 Dropbox 文件夹。|하루에 한 번, 선택한 폴더(예: OneDrive 또는 Dropbox 폴더)에 데이터를 복사합니다.",
        "Off: no folder chosen|Desactivado: sin carpeta|Désactivé : aucun dossier choisi|Aus: kein Ordner gewählt|Desativado: nenhuma pasta escolhida|Disattivato: nessuna cartella scelta|オフ: フォルダー未選択|关闭:未选择文件夹|꺼짐: 선택한 폴더 없음",
        "Choose folder…|Elegir carpeta…|Choisir un dossier…|Ordner wählen…|Escolher pasta…|Scegli cartella…|フォルダーを選択…|选择文件夹…|폴더 선택…",
        "Choose a backup folder|Elige una carpeta para las copias|Choisir un dossier de sauvegarde|Sicherungsordner wählen|Escolha uma pasta de backup|Scegli una cartella di backup|バックアップ先フォルダーを選択|选择备份文件夹|백업 폴더 선택",
        "That folder can't be written to.|No se puede escribir en esa carpeta.|Impossible d'écrire dans ce dossier.|In diesen Ordner kann nicht geschrieben werden.|Não é possível gravar nessa pasta.|Impossibile scrivere in questa cartella.|そのフォルダーには書き込めません。|无法写入该文件夹。|그 폴더에 쓸 수 없습니다.",
        "Back up now|Copiar ahora|Sauvegarder maintenant|Jetzt sichern|Fazer backup agora|Esegui backup ora|今すぐバックアップ|立即备份|지금 백업",
        "Turn off|Desactivar|Désactiver|Ausschalten|Desativar|Disattiva|オフにする|关闭|끄기",
        "{0} backups|{0} copias|{0} sauvegardes|{0} Sicherungen|{0} backups|{0} backup|{0} 件のバックアップ|{0} 个备份|백업 {0}개",
        "BACKUP FOLDER|CARPETA DE COPIAS|DOSSIER DE SAUVEGARDE|SICHERUNGSORDNER|PASTA DE BACKUP|CARTELLA DI BACKUP|バックアップ先|备份文件夹|백업 폴더",
        "KEEP THE LAST|CONSERVAR LAS ÚLTIMAS|GARDER LES DERNIÈRES|BEHALTE DIE LETZTEN|MANTER OS ÚLTIMOS|CONSERVA GLI ULTIMI|保存する件数|保留最近|최근 보관 개수",
        "Last backup: {0}|Última copia: {0}|Dernière sauvegarde : {0}|Letzte Sicherung: {0}|Último backup: {0}|Ultimo backup: {0}|前回のバックアップ: {0}|上次备份:{0}|마지막 백업: {0}",
    };
}
