namespace FinanceHub.Api.Features.Transactions.Commands.MarkTransfers;

public record MarkTransfersCommand(int[] TxIds, bool Unmark = false);
