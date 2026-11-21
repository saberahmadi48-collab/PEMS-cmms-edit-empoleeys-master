namespace PEMS.UI.Abstractions;

/// <summary>
/// Inputs of the pagination footer: where the user is, how big the list is and which URL each page link goes to.
/// BaseUrl is usually Request.Path; ExistingQueryString (without page/pageSize) keeps the other filters when the page changes.
/// PageSize, GridKey and AllowedPageSizes are optional: without a GridKey the rows-per-page selector is not shown.
/// </summary>
public class PaginationModel
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public string BaseUrl { get; set; } = "";
    public string ExistingQueryString { get; set; } = "";

    public string? GridKey { get; set; }
    public int PageSize { get; set; }
    public int[] AllowedPageSizes { get; set; } = [];

    public string BuildUrl(int page)
    {
        var qs = ExistingQueryString;
        return string.IsNullOrEmpty(qs) ? $"{BaseUrl}?page={page}" : $"{BaseUrl}?{qs}&page={page}";
    }

    // Changing the page size resets Page to 1 (Data Grid Standard, clause 10)
    public string BuildPageSizeUrl(int pageSize)
    {
        var qs = ExistingQueryString;
        return string.IsNullOrEmpty(qs)
            ? $"{BaseUrl}?pageSize={pageSize}&page=1"
            : $"{BaseUrl}?{qs}&pageSize={pageSize}&page=1";
    }
}

/// <summary>The words of the pagination footer, as inputs. <see cref="ShowingRecords"/> is a format with {0} first row, {1} last row, {2} total.</summary>
public sealed class PaginationTexts
{
    public TranslatableText ShowingRecords { get; init; } = TranslatableText.Empty;
    public TranslatableText NoResults { get; init; } = TranslatableText.Empty;
    public TranslatableText RowsPerPage { get; init; } = TranslatableText.Empty;
    public TranslatableText FirstPage { get; init; } = TranslatableText.Empty;
    public TranslatableText PreviousPage { get; init; } = TranslatableText.Empty;
    public TranslatableText NextPage { get; init; } = TranslatableText.Empty;
    public TranslatableText LastPage { get; init; } = TranslatableText.Empty;
}

/// <summary>Model of the PEMS.UI pagination partial: state plus texts.</summary>
public sealed record PaginationView(PaginationModel State, PaginationTexts Texts);
