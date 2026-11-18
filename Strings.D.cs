namespace Tempo;

// Columns: English | es | fr | de | pt | it | ja | zh | ko      (English text is the lookup key)
public static partial class Loc
{
    static readonly string[] RowsD =
    {
        // ---- tabs, timesheet ----
        "Entries|Registros|Entrées|Einträge|Registros|Registrazioni|記録|记录|기록",
        "Timesheet|Hoja de horas|Feuille de temps|Stundenzettel|Folha de horas|Foglio ore|タイムシート|工时表|타임시트",
        "Expenses|Gastos|Dépenses|Ausgaben|Despesas|Spese|経費|支出|경비",
        "Type hours into any cell. Tempo keeps them as a timesheet entry for that day.|Escribe horas en cualquier celda. Tempo las guarda como una entrada de hoja de horas para ese día.|Saisissez des heures dans n'importe quelle cellule. Tempo les conserve comme entrée de feuille de temps pour ce jour.|Trage Stunden in eine beliebige Zelle ein. Tempo speichert sie als Stundenzettel-Eintrag für diesen Tag.|Digite horas em qualquer célula. O Tempo as guarda como uma entrada da folha de horas daquele dia.|Scrivi le ore in una cella qualsiasi. Tempo le conserva come voce del foglio ore di quel giorno.|セルに時間を入力すると、その日のタイムシート記録として保存されます。|在任意单元格输入工时,Tempo 会将其保存为当天的工时表记录。|아무 칸에나 시간을 입력하면 해당 날짜의 타임시트 기록으로 저장됩니다.",
        "Previous week|Semana anterior|Semaine précédente|Vorwoche|Semana anterior|Settimana precedente|前の週|上一周|이전 주",
        "Next week|Semana siguiente|Semaine suivante|Folgewoche|Próxima semana|Settimana successiva|次の週|下一周|다음 주",
        "Week of {0}|Semana del {0}|Semaine du {0}|Woche vom {0}|Semana de {0}|Settimana del {0}|{0} の週|{0} 当周|{0} 주",
        "That day already has {0} h tracked on other entries.|Ese día ya tiene {0} h registradas en otros registros.|Ce jour compte déjà {0} h suivies sur d'autres entrées.|An diesem Tag sind bereits {0} Std. auf anderen Einträgen erfasst.|Esse dia já tem {0} h registradas em outros registros.|Quel giorno ha già {0} h su altre registrazioni.|その日はすでに他の記録で {0} 時間あります。|当天其他记录已有 {0} 小时。|그날은 다른 기록에 이미 {0}시간이 있습니다.",
        "That timesheet entry is already on an invoice.|Esa entrada de la hoja de horas ya está en una factura.|Cette entrée de feuille de temps est déjà sur une facture.|Dieser Stundenzettel-Eintrag ist bereits auf einer Rechnung.|Essa entrada da folha de horas já está em uma fatura.|Questa voce del foglio ore è già in una fattura.|そのタイムシート記録はすでに請求書にあります。|该工时表记录已在发票中。|해당 타임시트 기록은 이미 청구서에 있습니다.",

        // ---- expenses ----
        "Add expense|Añadir gasto|Ajouter une dépense|Ausgabe hinzufügen|Adicionar despesa|Aggiungi spesa|経費を追加|添加支出|경비 추가",
        "Edit expense|Editar gasto|Modifier la dépense|Ausgabe bearbeiten|Editar despesa|Modifica spesa|経費を編集|编辑支出|경비 편집",
        "Delete expense|Eliminar gasto|Supprimer la dépense|Ausgabe löschen|Excluir despesa|Elimina spesa|経費を削除|删除支出|경비 삭제",
        "No expenses yet|Aún no hay gastos|Aucune dépense pour l'instant|Noch keine Ausgaben|Nenhuma despesa ainda|Nessuna spesa per ora|経費はまだありません|还没有支出|아직 경비가 없습니다",
        "Track out-of-pocket costs for a project, attach the receipt, and bill them on the next invoice.|Registra los gastos de bolsillo de un proyecto, adjunta el recibo y facturalos en la siguiente factura.|Suivez les frais engagés pour un projet, joignez le reçu et facturez-les sur la prochaine facture.|Erfasse Auslagen für ein Projekt, hänge den Beleg an und rechne sie auf der nächsten Rechnung ab.|Registre custos do projeto, anexe o recibo e fature na próxima fatura.|Registra le spese sostenute per un progetto, allega la ricevuta e fatturale nella prossima fattura.|プロジェクトの立替費用を記録し、領収書を添付して、次の請求書に含められます。|记录项目垫付的费用,附上收据,并在下一张发票中开票。|프로젝트에 쓴 비용을 기록하고 영수증을 첨부해 다음 청구서에 청구하세요.",
        "RECEIPT (OPTIONAL)|RECIBO (OPCIONAL)|REÇU (FACULTATIF)|BELEG (OPTIONAL)|RECIBO (OPCIONAL)|RICEVUTA (FACOLTATIVO)|領収書(任意)|收据(可选)|영수증(선택)",
        "Choose file…|Elegir archivo…|Choisir un fichier…|Datei wählen…|Escolher arquivo…|Scegli file…|ファイルを選択…|选择文件…|파일 선택…",
        "No receipt|Sin recibo|Aucun reçu|Kein Beleg|Sem recibo|Nessuna ricevuta|領収書なし|无收据|영수증 없음",
        "Open receipt|Abrir recibo|Ouvrir le reçu|Beleg öffnen|Abrir recibo|Apri ricevuta|領収書を開く|打开收据|영수증 열기",
        "What was it for?|¿Para qué fue?|C'était pour quoi ?|Wofür war es?|Para que foi?|Per cosa era?|何の費用ですか?|用于什么?|무엇을 위한 것인가요?",
        "Enter an amount like 24.50.|Introduce un importe como 24,50.|Saisissez un montant comme 24,50.|Gib einen Betrag wie 24,50 ein.|Informe um valor como 24,50.|Inserisci un importo come 24,50.|24.50 のような金額を入力してください。|请输入金额,例如 24.50。|24.50 같은 금액을 입력하세요.",
        "Expense added|Gasto añadido|Dépense ajoutée|Ausgabe hinzugefügt|Despesa adicionada|Spesa aggiunta|経費を追加しました|已添加支出|경비를 추가했습니다",
        "Expense updated|Gasto actualizado|Dépense mise à jour|Ausgabe aktualisiert|Despesa atualizada|Spesa aggiornata|経費を更新しました|支出已更新|경비를 업데이트했습니다",
        "Expense deleted|Gasto eliminado|Dépense supprimée|Ausgabe gelöscht|Despesa excluída|Spesa eliminata|経費を削除しました|支出已删除|경비를 삭제했습니다",
        "{0} expense  ·  {1}|{0} gasto  ·  {1}|{0} dépense  ·  {1}|{0} Ausgabe  ·  {1}|{0} despesa  ·  {1}|{0} spesa  ·  {1}|経費 {0} 件  ·  {1}|{0} 笔支出  ·  {1}|경비 {0}건  ·  {1}",
        "{0} expenses  ·  {1}|{0} gastos  ·  {1}|{0} dépenses  ·  {1}|{0} Ausgaben  ·  {1}|{0} despesas  ·  {1}|{0} spese  ·  {1}|経費 {0} 件  ·  {1}|{0} 笔支出  ·  {1}|경비 {0}건  ·  {1}",

        // ---- reports ----
        "Reports|Informes|Rapports|Berichte|Relatórios|Report|レポート|报表|보고서",
        "See where your time and money went, by project, client, task, tag or period.|Mira en qué se fue tu tiempo y dinero, por proyecto, cliente, tarea, etiqueta o periodo.|Voyez où sont passés votre temps et votre argent, par projet, client, tâche, tag ou période.|Sieh, wohin Zeit und Geld geflossen sind, nach Projekt, Kunde, Aufgabe, Tag oder Zeitraum.|Veja para onde foram seu tempo e dinheiro, por projeto, cliente, tarefa, tag ou período.|Vedi dove sono andati tempo e denaro, per progetto, cliente, attività, tag o periodo.|時間とお金の行き先を、プロジェクト・クライアント・タスク・タグ・期間別に確認できます。|按项目、客户、任务、标签或时间段,查看时间和收入的去向。|프로젝트, 고객, 작업, 태그, 기간별로 시간과 수입이 어디에 쓰였는지 확인하세요.",
        "Last 30 days|Últimos 30 días|30 derniers jours|Letzte 30 Tage|Últimos 30 dias|Ultimi 30 giorni|過去30日|最近 30 天|최근 30일",
        "This year|Este año|Cette année|Dieses Jahr|Este ano|Quest'anno|今年|今年|올해",
        "All time|Todo el tiempo|Depuis toujours|Gesamte Zeit|Todo o período|Sempre|全期間|全部时间|전체 기간",
        "GROUP BY|AGRUPAR POR|GROUPER PAR|GRUPPIEREN NACH|AGRUPAR POR|RAGGRUPPA PER|グループ化|分组方式|그룹 기준",
        "Tag|Etiqueta|Tag|Tag|Tag|Tag|タグ|标签|태그",
        "Week|Semana|Semaine|Woche|Semana|Settimana|週|周|주",
        "Month|Mes|Mois|Monat|Mês|Mese|月|月|월",
        "(no tag)|(sin etiqueta)|(sans tag)|(kein Tag)|(sem tag)|(nessun tag)|(タグなし)|(无标签)|(태그 없음)",
        "All clients|Todos los clientes|Tous les clients|Alle Kunden|Todos os clientes|Tutti i clienti|すべてのクライアント|所有客户|모든 고객",
        "Tracked|Registrado|Suivi|Erfasst|Registrado|Tracciato|記録済み|已记录|기록됨",
        "Earned|Ingresos|Gagné|Verdient|Ganho|Guadagnato|収入|收入|수입",
        "{0}% of tracked time|{0} % del tiempo registrado|{0} % du temps suivi|{0} % der erfassten Zeit|{0}% do tempo registrado|{0}% del tempo tracciato|記録時間の {0}%|占已记录时间的 {0}%|기록 시간의 {0}%",
        "billable time only|solo tiempo facturable|temps facturable uniquement|nur abrechenbare Zeit|apenas tempo faturável|solo tempo fatturabile|請求対象の時間のみ|仅可开票时间|청구 대상 시간만",
        "in this range|en este rango|sur cette période|in diesem Zeitraum|neste período|in questo periodo|この期間|此范围内|이 기간",
        "Nothing tracked in this range.|Nada registrado en este rango.|Rien de suivi sur cette période.|In diesem Zeitraum wurde nichts erfasst.|Nada registrado neste período.|Niente tracciato in questo periodo.|この期間に記録はありません。|此范围内没有记录。|이 기간에 기록이 없습니다.",
        "Try another date range or clear a filter.|Prueba otro rango de fechas o quita un filtro.|Essayez une autre période ou retirez un filtre.|Wähle einen anderen Zeitraum oder entferne einen Filter.|Tente outro período ou remova um filtro.|Prova un altro periodo o rimuovi un filtro.|別の期間を選ぶか、フィルターを解除してください。|请换一个日期范围或清除筛选条件。|다른 기간을 선택하거나 필터를 지워 보세요.",
        "Export report PDF|Exportar informe en PDF|Exporter le rapport en PDF|Bericht als PDF exportieren|Exportar relatório em PDF|Esporta report in PDF|レポートをPDFで書き出す|导出报表 PDF|보고서 PDF 내보내기",
        "Time report|Informe de tiempo|Rapport de temps|Zeitbericht|Relatório de tempo|Report del tempo|時間レポート|工时报表|시간 보고서",
        "Report saved|Informe guardado|Rapport enregistré|Bericht gespeichert|Relatório salvo|Report salvato|レポートを保存しました|报表已保存|보고서를 저장했습니다",
        "GROUP|GRUPO|GROUPE|GRUPPE|GRUPO|GRUPPO|グループ|分组|그룹",
        "BILLABLE HOURS|HORAS FACTURABLES|HEURES FACTURABLES|ABRECHENBARE STD.|HORAS FATURÁVEIS|ORE FATTURABILI|請求対象時間|可开票工时|청구 대상 시간",
        "EARNED|INGRESOS|GAGNÉ|VERDIENT|GANHO|GUADAGNATO|収入|收入|수입",
    };
}
