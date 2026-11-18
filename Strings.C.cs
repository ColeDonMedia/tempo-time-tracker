namespace Tempo;

// Columns: English | es | fr | de | pt | it | ja | zh | ko      (English text is the lookup key)
public static partial class Loc
{
    static readonly string[] RowsC =
    {
        // ---- clients ----
        "Clients|Clientes|Clients|Kunden|Clientes|Clienti|クライアント|客户|고객",
        "People and brands you bill, with their default rate, terms and tax.|Personas y marcas a las que facturas, con su tarifa, condiciones e impuesto por defecto.|Les personnes et marques que vous facturez, avec tarif, conditions et taxe par défaut.|Personen und Marken, die du abrechnest, mit Standardsatz, Zahlungsbedingungen und Steuer.|Pessoas e marcas que você fatura, com valor, prazo e imposto padrão.|Persone e marchi che fatturi, con tariffa, termini e imposta predefiniti.|請求先の人やブランドと、標準の単価・支払条件・税率。|你开票的个人和品牌,及其默认费率、付款条件和税率。|청구 대상 사람과 브랜드, 기본 요율·결제 조건·세율.",
        "New client|Nuevo cliente|Nouveau client|Neuer Kunde|Novo cliente|Nuovo cliente|新規クライアント|新建客户|새 고객",
        "Edit client|Editar cliente|Modifier le client|Kunde bearbeiten|Editar cliente|Modifica cliente|クライアントを編集|编辑客户|고객 편집",
        "Create client|Crear cliente|Créer le client|Kunde anlegen|Criar cliente|Crea cliente|クライアントを作成|创建客户|고객 만들기",
        "Delete client|Eliminar cliente|Supprimer le client|Kunde löschen|Excluir cliente|Elimina cliente|クライアントを削除|删除客户|고객 삭제",
        "No clients yet|Aún no hay clientes|Aucun client pour l'instant|Noch keine Kunden|Nenhum cliente ainda|Nessun cliente per ora|クライアントはまだありません|还没有客户|아직 고객이 없습니다",
        "Add the people and brands you bill. Projects, invoices and rates can inherit their details.|Añade las personas y marcas a las que facturas. Proyectos, facturas y tarifas pueden heredar sus datos.|Ajoutez les personnes et marques que vous facturez. Projets, factures et tarifs peuvent hériter de leurs informations.|Lege die Personen und Marken an, die du abrechnest. Projekte, Rechnungen und Sätze übernehmen ihre Angaben.|Adicione as pessoas e marcas que você fatura. Projetos, faturas e valores herdam os dados deles.|Aggiungi le persone e i marchi che fatturi. Progetti, fatture e tariffe ereditano i loro dati.|請求先の人やブランドを追加すると、プロジェクト・請求書・単価がその情報を引き継ぎます。|添加你开票的个人和品牌,项目、发票和费率会沿用其信息。|청구 대상 사람과 브랜드를 추가하면 프로젝트, 청구서, 요율이 그 정보를 이어받습니다.",
        "Create your first client|Crea tu primer cliente|Créez votre premier client|Ersten Kunden anlegen|Crie seu primeiro cliente|Crea il tuo primo cliente|最初のクライアントを作成|创建第一个客户|첫 고객 만들기",
        "CLIENT|CLIENTE|CLIENT|KUNDE|CLIENTE|CLIENTE|クライアント|客户|고객",
        "＋ New client…|＋ Nuevo cliente…|＋ Nouveau client…|＋ Neuer Kunde…|＋ Novo cliente…|＋ Nuovo cliente…|＋ 新規クライアント…|＋ 新建客户…|＋ 새 고객…",
        "NAME|NOMBRE|NOM|NAME|NOME|NOME|名前|名称|이름",
        "DEFAULT RATE|TARIFA POR DEFECTO|TARIF PAR DÉFAUT|STANDARDSATZ|VALOR PADRÃO|TARIFFA PREDEFINITA|標準単価|默认费率|기본 요율",
        "Default (from Settings)|Por defecto (de Ajustes)|Par défaut (des Réglages)|Standard (aus den Einstellungen)|Padrão (das Configurações)|Predefinita (dalle Impostazioni)|標準(設定から)|默认(来自设置)|기본값(설정에서)",
        "NOTES|NOTAS|NOTES|NOTIZEN|NOTAS|NOTE|メモ|备注|메모",
        "Add a client name.|Añade un nombre de cliente.|Ajoutez un nom de client.|Gib einen Kundennamen ein.|Adicione um nome de cliente.|Aggiungi un nome di cliente.|クライアント名を入力してください。|请填写客户名称。|고객 이름을 입력하세요.",
        "Choose or create a client for this project.|Elige o crea un cliente para este proyecto.|Choisissez ou créez un client pour ce projet.|Wähle oder lege einen Kunden für dieses Projekt an.|Escolha ou crie um cliente para este projeto.|Scegli o crea un cliente per questo progetto.|このプロジェクトのクライアントを選択または作成してください。|请为此项目选择或创建客户。|이 프로젝트의 고객을 선택하거나 만드세요.",
        "{0} project|{0} proyecto|{0} projet|{0} Projekt|{0} projeto|{0} progetto|プロジェクト {0} 件|{0} 个项目|프로젝트 {0}개",
        "{0} projects|{0} proyectos|{0} projets|{0} Projekte|{0} projetos|{0} progetti|プロジェクト {0} 件|{0} 个项目|프로젝트 {0}개",
        "OUTSTANDING|PENDIENTE|EN ATTENTE|AUSSTEHEND|PENDENTE|IN SOSPESO|未払い|待付款|미결제",
        "Delete client “{0}”? Its projects are kept but will no longer be linked to a client.|¿Eliminar al cliente “{0}”? Sus proyectos se conservan pero dejan de estar vinculados a un cliente.|Supprimer le client « {0} » ? Ses projets sont conservés mais ne seront plus liés à un client.|Kunde „{0}“ löschen? Seine Projekte bleiben erhalten, sind aber keinem Kunden mehr zugeordnet.|Excluir o cliente “{0}”? Os projetos dele são mantidos, mas deixam de ter cliente vinculado.|Eliminare il cliente “{0}”? I suoi progetti restano ma non saranno più collegati a un cliente.|クライアント「{0}」を削除しますか?プロジェクトは残りますが、クライアントとの紐づけは外れます。|删除客户“{0}”吗?其项目会保留,但不再关联客户。|고객 “{0}”을(를) 삭제할까요? 프로젝트는 유지되지만 고객과의 연결이 해제됩니다.",
        "Client “{0}” deleted|Cliente “{0}” eliminado|Client « {0} » supprimé|Kunde „{0}“ gelöscht|Cliente “{0}” excluído|Cliente “{0}” eliminato|クライアント「{0}」を削除しました|已删除客户“{0}”|고객 “{0}”을(를) 삭제했습니다",
        "Client “{0}” created|Cliente “{0}” creado|Client « {0} » créé|Kunde „{0}“ angelegt|Cliente “{0}” criado|Cliente “{0}” creato|クライアント「{0}」を作成しました|已创建客户“{0}”|고객 “{0}”을(를) 만들었습니다",
        "Client updated|Cliente actualizado|Client mis à jour|Kunde aktualisiert|Cliente atualizado|Cliente aggiornato|クライアントを更新しました|客户已更新|고객을 업데이트했습니다",

        // ---- billable, tags ----
        "Billable|Facturable|Facturable|Abrechenbar|Faturável|Fatturabile|請求対象|可开票|청구 대상",
        "Non-billable|No facturable|Non facturable|Nicht abrechenbar|Não faturável|Non fatturabile|請求対象外|不可开票|청구 제외",
        "Billable by default|Facturable por defecto|Facturable par défaut|Standardmäßig abrechenbar|Faturável por padrão|Fatturabile per impostazione predefinita|標準で請求対象|默认可开票|기본적으로 청구 대상",
        "TAGS (COMMA SEPARATED)|ETIQUETAS (SEPARADAS POR COMAS)|TAGS (SÉPARÉS PAR DES VIRGULES)|TAGS (DURCH KOMMA GETRENNT)|TAGS (SEPARADAS POR VÍRGULA)|TAG (SEPARATI DA VIRGOLA)|タグ(カンマ区切り)|标签(逗号分隔)|태그(쉼표로 구분)",
        "design, meeting|diseño, reunión|design, réunion|Design, Meeting|design, reunião|design, riunione|デザイン, 会議|设计, 会议|디자인, 회의",

        // ---- budgets ----
        "BUDGET|PRESUPUESTO|BUDGET|BUDGET|ORÇAMENTO|BUDGET|予算|预算|예산",
        "BUDGET TYPE|TIPO DE PRESUPUESTO|TYPE DE BUDGET|BUDGETART|TIPO DE ORÇAMENTO|TIPO DI BUDGET|予算の種類|预算类型|예산 유형",
        "None|Ninguno|Aucun|Keins|Nenhum|Nessuno|なし|无|없음",
        "Hours|Horas|Heures|Stunden|Horas|Ore|時間|工时|시간",
        "Amount|Importe|Montant|Betrag|Valor|Importo|金額|金额|금액",
        "Fixed fee|Tarifa fija|Forfait|Festpreis|Valor fixo|Tariffa fissa|固定報酬|固定费用|고정 금액",
        "Retainer|Iguala|Forfait mensuel|Retainer|Retainer|Retainer|月額契約|长期服务费|리테이너",
        "Total|Total|Total|Gesamt|Total|Totale|合計|总计|합계",
        "Per month|Por mes|Par mois|Pro Monat|Por mês|Al mese|月ごと|每月|월별",
        "BUDGET (HOURS)|PRESUPUESTO (HORAS)|BUDGET (HEURES)|BUDGET (STUNDEN)|ORÇAMENTO (HORAS)|BUDGET (ORE)|予算(時間)|预算(小时)|예산(시간)",
        "BUDGET ({0})|PRESUPUESTO ({0})|BUDGET ({0})|BUDGET ({0})|ORÇAMENTO ({0})|BUDGET ({0})|予算({0})|预算({0})|예산({0})",
        "FIXED FEE ({0})|TARIFA FIJA ({0})|FORFAIT ({0})|FESTPREIS ({0})|VALOR FIXO ({0})|TARIFFA FISSA ({0})|固定報酬({0})|固定费用({0})|고정 금액({0})",
        "MONTHLY FEE ({0})|CUOTA MENSUAL ({0})|FORFAIT MENSUEL ({0})|MONATSPAUSCHALE ({0})|VALOR MENSAL ({0})|CANONE MENSILE ({0})|月額({0})|月费({0})|월 요금({0})",
        "Enter a budget like 40 or 1500.|Introduce un presupuesto como 40 o 1500.|Saisissez un budget comme 40 ou 1500.|Gib ein Budget wie 40 oder 1500 ein.|Informe um orçamento como 40 ou 1500.|Inserisci un budget come 40 o 1500.|40 や 1500 のような予算を入力してください。|请输入预算,例如 40 或 1500。|40 또는 1500 같은 예산을 입력하세요.",
        "{0} of {1} h used|{0} de {1} h usadas|{0} sur {1} h utilisées|{0} von {1} Std. verbraucht|{0} de {1} h usadas|{0} di {1} h usate|{1} 時間中 {0} 時間を使用|已用 {0} / {1} 小时|{1}시간 중 {0}시간 사용",
        "{0} of {1} used|{0} de {1} usados|{0} sur {1} utilisés|{0} von {1} verbraucht|{0} de {1} usados|{0} di {1} usati|{1} 中 {0} を使用|已用 {0} / {1}|{1} 중 {0} 사용",
        "Effective rate {0} / h|Tarifa efectiva {0} / h|Tarif effectif {0} / h|Effektiver Satz {0} / Std.|Valor efetivo {0} / h|Tariffa effettiva {0} / h|実効単価 {0} / 時間|实际费率 {0} / 小时|실제 요율 {0} / 시간",
        "{0} has used 80% of its budget|{0} ha usado el 80 % de su presupuesto|{0} a utilisé 80 % de son budget|{0} hat 80 % des Budgets verbraucht|{0} usou 80% do orçamento|{0} ha usato l'80% del budget|{0} は予算の80%を使いました|{0} 已用掉预算的 80%|{0}이(가) 예산의 80%를 사용했습니다",
        "{0} is over budget|{0} superó el presupuesto|{0} dépasse son budget|{0} hat das Budget überschritten|{0} estourou o orçamento|{0} ha superato il budget|{0} は予算を超えました|{0} 已超出预算|{0}이(가) 예산을 초과했습니다",

        // ---- trash / undo ----
        "Undo|Deshacer|Annuler|Rückgängig|Desfazer|Annulla|元に戻す|撤销|실행 취소",
        "Restored|Restaurado|Restauré|Wiederhergestellt|Restaurado|Ripristinato|復元しました|已恢复|복원되었습니다",
        "Recently deleted|Eliminado recientemente|Supprimés récemment|Kürzlich gelöscht|Excluídos recentemente|Eliminati di recente|最近削除した項目|最近删除|최근 삭제됨",
        "Anything you delete waits here so you can bring it back. Nothing is removed for good until you empty this list.|Lo que eliminas espera aquí para poder recuperarlo. Nada se borra definitivamente hasta que vacíes esta lista.|Ce que vous supprimez attend ici pour pouvoir être récupéré. Rien n'est effacé définitivement tant que vous ne videz pas cette liste.|Was du löschst, wartet hier und lässt sich zurückholen. Erst wenn du die Liste leerst, ist es endgültig weg.|O que você exclui espera aqui para ser recuperado. Nada some de vez até você esvaziar esta lista.|Ciò che elimini resta qui così puoi recuperarlo. Niente viene rimosso definitivamente finché non svuoti questo elenco.|削除した項目はここに残り、元に戻せます。このリストを空にするまで完全には消えません。|删除的内容会先放在这里,可随时恢复。清空此列表前不会被永久删除。|삭제한 항목은 여기에 보관되어 되돌릴 수 있습니다. 이 목록을 비우기 전에는 완전히 삭제되지 않습니다.",
        "Nothing deleted recently.|Nada eliminado recientemente.|Rien de supprimé récemment.|Zuletzt wurde nichts gelöscht.|Nada excluído recentemente.|Niente eliminato di recente.|最近削除した項目はありません。|最近没有删除内容。|최근 삭제한 항목이 없습니다.",
        "Delete forever|Eliminar para siempre|Supprimer définitivement|Endgültig löschen|Excluir para sempre|Elimina per sempre|完全に削除|永久删除|영구 삭제",
        "Empty list|Vaciar lista|Vider la liste|Liste leeren|Esvaziar lista|Svuota elenco|リストを空にする|清空列表|목록 비우기",
        "Time entry|Registro de tiempo|Entrée de temps|Zeiteintrag|Registro de tempo|Registrazione|時間記録|工时记录|시간 기록",
        "Project|Proyecto|Projet|Projekt|Projeto|Progetto|プロジェクト|项目|프로젝트",
        "Client|Cliente|Client|Kunde|Cliente|Cliente|クライアント|客户|고객",
        "Expense|Gasto|Dépense|Ausgabe|Despesa|Spesa|経費|支出|경비",
        "Delete “{0}”?|¿Eliminar “{0}”?|Supprimer « {0} » ?|„{0}“ löschen?|Excluir “{0}”?|Eliminare “{0}”?|「{0}」を削除しますか?|删除“{0}”吗?|“{0}”을(를) 삭제할까요?",
        "You can restore it from Settings → Recently deleted. Archiving hides a project without deleting anything.|Puedes restaurarlo desde Ajustes → Eliminado recientemente. Archivar oculta un proyecto sin eliminar nada.|Vous pourrez le restaurer depuis Réglages → Supprimés récemment. Archiver masque un projet sans rien supprimer.|Du kannst es unter Einstellungen → Kürzlich gelöscht wiederherstellen. Archivieren blendet ein Projekt aus, ohne etwas zu löschen.|Você pode restaurar em Configurações → Excluídos recentemente. Arquivar oculta um projeto sem excluir nada.|Puoi ripristinarlo da Impostazioni → Eliminati di recente. Archiviare nasconde un progetto senza eliminare nulla.|設定 → 最近削除した項目から復元できます。アーカイブなら何も削除せずに非表示にできます。|可在“设置 → 最近删除”中恢复。归档只会隐藏项目,不会删除任何内容。|설정 → 최근 삭제됨에서 복원할 수 있습니다. 보관하면 아무것도 삭제하지 않고 프로젝트를 숨길 수 있습니다.",
        "Delete {0} ({1})?|¿Eliminar {0} ({1})?|Supprimer {0} ({1}) ?|{0} ({1}) löschen?|Excluir {0} ({1})?|Eliminare {0} ({1})?|{0}({1})を削除しますか?|删除 {0}({1})吗?|{0}({1})을(를) 삭제할까요?",
        "You can restore it from Settings → Recently deleted. The PDF file you exported is not touched.|Puedes restaurarla desde Ajustes → Eliminado recientemente. El archivo PDF exportado no se modifica.|Vous pourrez la restaurer depuis Réglages → Supprimés récemment. Le fichier PDF exporté n'est pas modifié.|Du kannst sie unter Einstellungen → Kürzlich gelöscht wiederherstellen. Die exportierte PDF-Datei bleibt unberührt.|Você pode restaurar em Configurações → Excluídos recentemente. O arquivo PDF exportado não é alterado.|Puoi ripristinarla da Impostazioni → Eliminati di recente. Il file PDF esportato non viene toccato.|設定 → 最近削除した項目から復元できます。書き出したPDFファイルは変更されません。|可在“设置 → 最近删除”中恢复。已导出的 PDF 文件不会受影响。|설정 → 최근 삭제됨에서 복원할 수 있습니다. 내보낸 PDF 파일은 그대로 유지됩니다.",
    };
}
