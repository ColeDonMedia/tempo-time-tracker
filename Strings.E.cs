namespace Tempo;

// Columns: English | es | fr | de | pt | it | ja | zh | ko      (English text is the lookup key)
public static partial class Loc
{
    static readonly string[] RowsE =
    {
        // ---- tabs and headings ----
        "Quotes|Presupuestos|Devis|Angebote|Orçamentos|Preventivi|見積書|报价单|견적서",
        "Recurring|Recurrentes|Récurrentes|Wiederkehrend|Recorrentes|Ricorrenti|定期|定期|정기",
        "Send a price before the work starts. Accepted quotes turn into invoices in one click.|Envía un precio antes de empezar. Los presupuestos aceptados se convierten en facturas con un clic.|Envoyez un prix avant de commencer. Les devis acceptés deviennent des factures en un clic.|Nenne den Preis, bevor die Arbeit beginnt. Angenommene Angebote werden mit einem Klick zu Rechnungen.|Envie um preço antes de começar. Orçamentos aceitos viram faturas com um clique.|Invia un prezzo prima di iniziare. I preventivi accettati diventano fatture con un clic.|作業前に金額を提示できます。承諾された見積書はワンクリックで請求書になります。|在开工前先报价。已接受的报价单一键转为发票。|작업 전에 금액을 제안하세요. 수락된 견적서는 클릭 한 번으로 청구서가 됩니다.",
        "Retainers and repeat work: Tempo creates each invoice on its due date.|Igualas y trabajo repetido: Tempo crea cada factura en su fecha.|Forfaits et travaux récurrents : Tempo crée chaque facture à son échéance.|Retainer und Wiederholungsarbeit: Tempo erstellt jede Rechnung zum Fälligkeitstermin.|Retainers e trabalho recorrente: o Tempo cria cada fatura na data certa.|Retainer e lavoro ricorrente: Tempo crea ogni fattura alla scadenza.|月額契約や繰り返しの仕事に。Tempo が期日に請求書を自動作成します。|长期服务和重复工作:Tempo 会在到期日自动创建每张发票。|리테이너와 반복 작업: Tempo가 예정일에 청구서를 자동으로 만듭니다.",
        "Quote details|Detalles del presupuesto|Détails du devis|Angebotsdetails|Detalhes do orçamento|Dettagli del preventivo|見積書の詳細|报价单详情|견적서 세부 정보",
        "QUOTE NUMBER|NÚMERO DE PRESUPUESTO|NUMÉRO DE DEVIS|ANGEBOTSNUMMER|NÚMERO DO ORÇAMENTO|NUMERO PREVENTIVO|見積番号|报价单编号|견적서 번호",
        "VALID FOR (DAYS)|VÁLIDO POR (DÍAS)|VALIDITÉ (JOURS)|GÜLTIG FÜR (TAGE)|VÁLIDO POR (DIAS)|VALIDO PER (GIORNI)|有効期間(日数)|有效期(天)|유효 기간(일)",
        "QUOTE|PRESUPUESTO|DEVIS|ANGEBOT|ORÇAMENTO|PREVENTIVO|見積書|报价单|견적서",
        "Valid until|Válido hasta|Valable jusqu'au|Gültig bis|Válido até|Valido fino al|有効期限|有效期至|유효 기한",
        "Export quote PDF|Exportar presupuesto en PDF|Exporter le devis en PDF|Angebot als PDF exportieren|Exportar orçamento em PDF|Esporta preventivo in PDF|見積書をPDFで書き出す|导出报价单 PDF|견적서 PDF 내보내기",
        "Quote {0} exported|Presupuesto {0} exportado|Devis {0} exporté|Angebot {0} exportiert|Orçamento {0} exportado|Preventivo {0} esportato|見積書 {0} を書き出しました|已导出报价单 {0}|견적서 {0} 내보냄",
        "Quote history|Historial de presupuestos|Historique des devis|Angebotsverlauf|Histórico de orçamentos|Storico preventivi|見積書履歴|报价单历史|견적서 내역",
        "Track which quotes were sent, accepted or declined.|Sigue qué presupuestos se enviaron, aceptaron o rechazaron.|Suivez les devis envoyés, acceptés ou refusés.|Behalte im Blick, welche Angebote gesendet, angenommen oder abgelehnt wurden.|Acompanhe quais orçamentos foram enviados, aceitos ou recusados.|Tieni traccia dei preventivi inviati, accettati o rifiutati.|送付・承諾・辞退された見積書を管理します。|跟踪报价单的发送、接受或拒绝状态。|견적서의 발송·수락·거절 상태를 확인하세요.",
        "No quotes yet|Aún no hay presupuestos|Aucun devis pour l'instant|Noch keine Angebote|Nenhum orçamento ainda|Nessun preventivo per ora|見積書はまだありません|还没有报价单|아직 견적서가 없습니다",
        "Quotes you export appear here. When a client says yes, convert it to an invoice in one click.|Los presupuestos que exportes aparecen aquí. Cuando un cliente acepte, conviértelo en factura con un clic.|Les devis exportés apparaissent ici. Quand un client accepte, convertissez-le en facture en un clic.|Exportierte Angebote erscheinen hier. Sagt ein Kunde zu, wandelst du es mit einem Klick in eine Rechnung um.|Os orçamentos exportados aparecem aqui. Quando o cliente aceitar, converta em fatura com um clique.|I preventivi esportati compaiono qui. Quando un cliente accetta, convertilo in fattura con un clic.|書き出した見積書がここに表示されます。クライアントが承諾したらワンクリックで請求書に変換できます。|导出的报价单会显示在这里。客户同意后,一键转为发票。|내보낸 견적서가 여기에 표시됩니다. 고객이 수락하면 클릭 한 번으로 청구서로 변환하세요.",
        "Sent|Enviado|Envoyé|Gesendet|Enviado|Inviato|送付済み|已发送|발송됨",
        "Accepted|Aceptado|Accepté|Angenommen|Aceito|Accettato|承諾済み|已接受|수락됨",
        "Declined|Rechazado|Refusé|Abgelehnt|Recusado|Rifiutato|辞退|已拒绝|거절됨",
        "Mark accepted|Marcar como aceptado|Marquer comme accepté|Als angenommen markieren|Marcar como aceito|Segna come accettato|承諾済みにする|标记为已接受|수락됨으로 표시",
        "Mark declined|Marcar como rechazado|Marquer comme refusé|Als abgelehnt markieren|Marcar como recusado|Segna come rifiutato|辞退にする|标记为已拒绝|거절됨으로 표시",
        "Convert to invoice|Convertir en factura|Convertir en facture|In Rechnung umwandeln|Converter em fatura|Converti in fattura|請求書に変換|转为发票|청구서로 변환",
        "Invoice {0} created from quote {1}|Factura {0} creada a partir del presupuesto {1}|Facture {0} créée à partir du devis {1}|Rechnung {0} aus Angebot {1} erstellt|Fatura {0} criada a partir do orçamento {1}|Fattura {0} creata dal preventivo {1}|見積書 {1} から請求書 {0} を作成しました|已根据报价单 {1} 创建发票 {0}|견적서 {1}에서 청구서 {0}을(를) 만들었습니다",
        "{0}  ·  Issued {1}  ·  Valid until {2}|{0}  ·  Emitido {1}  ·  Válido hasta {2}|{0}  ·  Émis le {1}  ·  Valable jusqu'au {2}|{0}  ·  Ausgestellt {1}  ·  Gültig bis {2}|{0}  ·  Emitido {1}  ·  Válido até {2}|{0}  ·  Emesso {1}  ·  Valido fino al {2}|{0}  ·  発行 {1}  ·  有効期限 {2}|{0}  ·  开具 {1}  ·  有效期至 {2}|{0}  ·  발행 {1}  ·  유효 기한 {2}",

        // ---- builder ----
        "TEMPLATE|PLANTILLA|MODÈLE|VORLAGE|MODELO|MODELLO|テンプレート|模板|템플릿",
        "Classic|Clásica|Classique|Klassisch|Clássico|Classico|クラシック|经典|클래식",
        "Minimal|Minimalista|Minimal|Minimal|Minimalista|Minimale|ミニマル|简约|미니멀",
        "Bold|Intensa|Marqué|Kräftig|Marcante|Deciso|ボールド|醒目|볼드",
        "PAY LINK (OPTIONAL)|ENLACE DE PAGO (OPCIONAL)|LIEN DE PAIEMENT (FACULTATIF)|ZAHLUNGSLINK (OPTIONAL)|LINK DE PAGAMENTO (OPCIONAL)|LINK DI PAGAMENTO (FACOLTATIVO)|支払いリンク(任意)|付款链接(可选)|결제 링크(선택)",
        "Include tracked time|Incluir tiempo registrado|Inclure le temps suivi|Erfasste Zeit einbeziehen|Incluir tempo registrado|Includi il tempo tracciato|記録した時間を含める|包含已记录的工时|기록된 시간 포함",
        "Include billable expenses|Incluir gastos facturables|Inclure les dépenses facturables|Abrechenbare Ausgaben einbeziehen|Incluir despesas faturáveis|Includi le spese fatturabili|請求対象の経費を含める|包含可开票支出|청구 대상 경비 포함",
        "Attach a timesheet page|Adjuntar una página de hoja de horas|Joindre une page de feuille de temps|Stundenzettel-Seite anhängen|Anexar uma página de folha de horas|Allega una pagina di foglio ore|タイムシートのページを添付|附加工时表页面|타임시트 페이지 첨부",
        "Bill the fixed fee|Facturar la tarifa fija|Facturer le forfait|Festpreis abrechnen|Faturar o valor fixo|Fattura la tariffa fissa|固定報酬を請求|开具固定费用|고정 금액 청구",
        "LINE ITEMS|LÍNEAS|LIGNES|POSITIONEN|LINHAS|RIGHE|明細|明细|항목",
        "Add line|Añadir línea|Ajouter une ligne|Position hinzufügen|Adicionar linha|Aggiungi riga|行を追加|添加一行|항목 추가",
        "Description|Descripción|Description|Beschreibung|Descrição|Descrizione|説明|描述|설명",
        "Qty|Cant.|Qté|Menge|Qtd.|Qtà|数量|数量|수량",
        "Price|Precio|Prix|Preis|Preço|Prezzo|単価|单价|단가",
        "Remove|Quitar|Retirer|Entfernen|Remover|Rimuovi|削除|移除|제거",
        "Add at least one line item.|Añade al menos una línea.|Ajoutez au moins une ligne.|Füge mindestens eine Position hinzu.|Adicione pelo menos uma linha.|Aggiungi almeno una riga.|明細を1行以上追加してください。|请至少添加一行明细。|항목을 하나 이상 추가하세요.",
        "Expense: {0}|Gasto: {0}|Dépense : {0}|Auslage: {0}|Despesa: {0}|Spesa: {0}|経費: {0}|支出:{0}|경비: {0}",

        // ---- payments, late fees ----
        "Record payment|Registrar pago|Enregistrer un paiement|Zahlung erfassen|Registrar pagamento|Registra pagamento|入金を記録|记录付款|결제 기록",
        "PAYMENT DATE|FECHA DEL PAGO|DATE DU PAIEMENT|ZAHLUNGSDATUM|DATA DO PAGAMENTO|DATA DEL PAGAMENTO|入金日|付款日期|결제일",
        "NOTE (OPTIONAL)|NOTA (OPCIONAL)|NOTE (FACULTATIF)|NOTIZ (OPTIONAL)|NOTA (OPCIONAL)|NOTA (FACOLTATIVO)|メモ(任意)|备注(可选)|메모(선택)",
        "Payments|Pagos|Paiements|Zahlungen|Pagamentos|Pagamenti|入金|付款记录|결제 내역",
        "Remove payment|Quitar pago|Retirer le paiement|Zahlung entfernen|Remover pagamento|Rimuovi pagamento|入金を削除|移除付款|결제 삭제",
        "Balance due|Saldo pendiente|Solde dû|Offener Betrag|Saldo devedor|Saldo dovuto|未払い残高|应付余额|미결제 잔액",
        "BALANCE DUE|SALDO PENDIENTE|SOLDE DÛ|OFFENER BETRAG|SALDO DEVEDOR|SALDO DOVUTO|未払い残高|应付余额|미결제 잔액",
        "Paid to date|Pagado hasta ahora|Payé à ce jour|Bisher bezahlt|Pago até agora|Pagato finora|入金済み|已付金额|지금까지 결제",
        "Partial|Parcial|Partiel|Teilweise|Parcial|Parziale|一部入金|部分付款|부분 결제",
        "Payment recorded|Pago registrado|Paiement enregistré|Zahlung erfasst|Pagamento registrado|Pagamento registrato|入金を記録しました|已记录付款|결제를 기록했습니다",
        "Enter an amount greater than zero.|Introduce un importe mayor que cero.|Saisissez un montant supérieur à zéro.|Gib einen Betrag über null ein.|Informe um valor maior que zero.|Inserisci un importo maggiore di zero.|0より大きい金額を入力してください。|请输入大于零的金额。|0보다 큰 금액을 입력하세요.",
        "Late fee|Recargo por mora|Pénalité de retard|Mahngebühr|Multa por atraso|Mora|延滞料|滞纳金|연체료",
        "Apply late fee ({0}%)|Aplicar recargo por mora ({0} %)|Appliquer la pénalité de retard ({0} %)|Mahngebühr anwenden ({0} %)|Aplicar multa por atraso ({0}%)|Applica mora ({0}%)|延滞料を適用({0}%)|应用滞纳金({0}%)|연체료 적용({0}%)",
        "Late fee of {0} added to {1}|Recargo de {0} añadido a {1}|Pénalité de {0} ajoutée à {1}|Mahngebühr von {0} zu {1} hinzugefügt|Multa de {0} adicionada a {1}|Mora di {0} aggiunta a {1}|{1} に延滞料 {0} を追加しました|已为 {1} 添加滞纳金 {0}|{1}에 연체료 {0}을(를) 추가했습니다",

        // ---- PDF ----
        "PAY ONLINE|PAGAR EN LÍNEA|PAYER EN LIGNE|ONLINE BEZAHLEN|PAGAR ONLINE|PAGA ONLINE|オンラインで支払う|在线付款|온라인 결제",
        "Scan to pay|Escanea para pagar|Scannez pour payer|Zum Bezahlen scannen|Escaneie para pagar|Scansiona per pagare|スキャンして支払う|扫码付款|스캔하여 결제",
        "TIMESHEET|HOJA DE HORAS|FEUILLE DE TEMPS|STUNDENZETTEL|FOLHA DE HORAS|FOGLIO ORE|タイムシート|工时表|타임시트",

        // ---- recurring ----
        "New recurring invoice|Nueva factura recurrente|Nouvelle facture récurrente|Neue wiederkehrende Rechnung|Nova fatura recorrente|Nuova fattura ricorrente|新しい定期請求書|新建定期发票|새 정기 청구서",
        "Edit recurring invoice|Editar factura recurrente|Modifier la facture récurrente|Wiederkehrende Rechnung bearbeiten|Editar fatura recorrente|Modifica fattura ricorrente|定期請求書を編集|编辑定期发票|정기 청구서 편집",
        "Recurring invoices|Facturas recurrentes|Factures récurrentes|Wiederkehrende Rechnungen|Faturas recorrentes|Fatture ricorrenti|定期請求書|定期发票|정기 청구서",
        "No recurring invoices yet|Aún no hay facturas recurrentes|Aucune facture récurrente pour l'instant|Noch keine wiederkehrenden Rechnungen|Nenhuma fatura recorrente ainda|Nessuna fattura ricorrente per ora|定期請求書はまだありません|还没有定期发票|아직 정기 청구서가 없습니다",
        "FREQUENCY|FRECUENCIA|FRÉQUENCE|HÄUFIGKEIT|FREQUÊNCIA|FREQUENZA|頻度|频率|주기",
        "Weekly|Semanal|Hebdomadaire|Wöchentlich|Semanal|Settimanale|毎週|每周|매주",
        "Monthly|Mensual|Mensuel|Monatlich|Mensal|Mensile|毎月|每月|매월",
        "Quarterly|Trimestral|Trimestriel|Vierteljährlich|Trimestral|Trimestrale|四半期ごと|每季度|분기별",
        "NEXT INVOICE DATE|FECHA DE LA PRÓXIMA FACTURA|DATE DE LA PROCHAINE FACTURE|NÄCHSTES RECHNUNGSDATUM|DATA DA PRÓXIMA FATURA|DATA PROSSIMA FATTURA|次回の請求日|下次开票日期|다음 청구일",
        "Active|Activa|Active|Aktiv|Ativa|Attiva|有効|启用|활성",
        "Paused|En pausa|En pause|Pausiert|Pausada|In pausa|一時停止中|已暂停|일시정지됨",
        "Run now|Ejecutar ahora|Exécuter maintenant|Jetzt ausführen|Executar agora|Esegui ora|今すぐ実行|立即运行|지금 실행",
        "Next: {0}|Próxima: {0}|Prochaine : {0}|Nächste: {0}|Próxima: {0}|Prossima: {0}|次回: {0}|下次:{0}|다음: {0}",
        "Created {0}|Creada {0}|Créée {0}|Erstellt {0}|Criada {0}|Creata {0}|{0} を作成しました|已创建 {0}|{0} 생성됨",
        "Recurring invoice saved|Factura recurrente guardada|Facture récurrente enregistrée|Wiederkehrende Rechnung gespeichert|Fatura recorrente salva|Fattura ricorrente salvata|定期請求書を保存しました|定期发票已保存|정기 청구서를 저장했습니다",
        "{0} recurring invoice(s) created. Open Invoices to export them.|{0} factura(s) recurrente(s) creada(s). Abre Facturas para exportarlas.|{0} facture(s) récurrente(s) créée(s). Ouvrez Factures pour les exporter.|{0} wiederkehrende Rechnung(en) erstellt. Öffne Rechnungen, um sie zu exportieren.|{0} fatura(s) recorrente(s) criada(s). Abra Faturas para exportá-las.|{0} fattura/e ricorrente/i creata/e. Apri Fatture per esportarle.|定期請求書を {0} 件作成しました。請求書ページから書き出せます。|已创建 {0} 张定期发票。请在发票页面导出。|정기 청구서 {0}건을 만들었습니다. 청구서에서 내보내세요.",

        // ---- settings ----
        "LATE FEE (%)|RECARGO POR MORA (%)|PÉNALITÉ DE RETARD (%)|MAHNGEBÜHR (%)|MULTA POR ATRASO (%)|MORA (%)|延滞料(%)|滞纳金(%)|연체료(%)",
        "GRACE PERIOD (DAYS)|PERIODO DE GRACIA (DÍAS)|DÉLAI DE GRÂCE (JOURS)|KULANZFRIST (TAGE)|CARÊNCIA (DIAS)|PERIODO DI GRAZIA (GIORNI)|猶予期間(日数)|宽限期(天)|유예 기간(일)",
        "DEFAULT TEMPLATE|PLANTILLA PREDETERMINADA|MODÈLE PAR DÉFAUT|STANDARDVORLAGE|MODELO PADRÃO|MODELLO PREDEFINITO|標準テンプレート|默认模板|기본 템플릿",
        "DEFAULT PAY LINK|ENLACE DE PAGO PREDETERMINADO|LIEN DE PAIEMENT PAR DÉFAUT|STANDARD-ZAHLUNGSLINK|LINK DE PAGAMENTO PADRÃO|LINK DI PAGAMENTO PREDEFINITO|標準の支払いリンク|默认付款链接|기본 결제 링크",
    };
}
