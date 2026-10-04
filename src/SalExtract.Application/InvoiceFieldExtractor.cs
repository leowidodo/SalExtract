using System.Globalization;
using System.Text.RegularExpressions;
using SalExtract.Domain;

namespace SalExtract.Application;

public sealed partial class InvoiceFieldExtractor : IInvoiceFieldExtractor
{
    private static readonly string[] SupplierLabels = ["supplier", "vendor", "from", "penjual", "nama perusahaan"];
    private static readonly string[] InvoiceLabels = ["invoice no", "invoice number", "no. invoice", "no invoice", "nomor invoice", "no. faktur", "nomor faktur", "invoice #", "faktur no", "no. nota", "no nota", "nomor nota", "nota no", "invoice"];
    private static readonly string[] DateLabels = ["invoice date", "tanggal invoice", "tanggal faktur", "date", "tanggal", "tgl.", "tgl"];
    private static readonly string[] TotalLabels = ["grand total", "total invoice", "invoice total", "total tagihan", "jumlah total", "amount due", "balance due", "total due", "total"];
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");

    public IReadOnlyDictionary<string, ExtractedField> Extract(DocumentContent document)
    {
        var lines = document.Pages.SelectMany(p => NormalizeLines(p.Text).Select(line => (p.PageNumber, Line: line))).ToList();
        var total = ExtractTotal(lines);
        var fields = new Dictionary<string, ExtractedField>(StringComparer.OrdinalIgnoreCase)
        {
            ["SupplierName"] = ExtractSupplier(lines),
            ["InvoiceNo"] = ExtractInvoiceNumber(lines),
            ["InvoiceDate"] = ExtractInvoiceDate(lines),
            ["TotalInvoice"] = total,
            ["Currency"] = ExtractCurrency(lines, total)
        };
        return fields;
    }

    private static ExtractedField ExtractCurrency(List<(int Page, string Line)> lines, ExtractedField total)
    {
        var dollar = lines.FirstOrDefault(x => x.Line.Contains('$'));
        if (!string.IsNullOrWhiteSpace(dollar.Line))
        {
            var totalUsesDollar = total.RawValue?.Contains('$') == true && !string.IsNullOrWhiteSpace(total.Evidence);
            var evidence = totalUsesDollar ? total.Evidence : dollar.Line;
            return Field("Currency", "$", "USD", .99m, totalUsesDollar ? total.Page ?? dollar.Page : dollar.Page, evidence!);
        }

        var rupiah = lines.FirstOrDefault(x => RupiahMarkerRegex().IsMatch(x.Line));
        if (!string.IsNullOrWhiteSpace(rupiah.Line))
        {
            var marker = RupiahMarkerRegex().Match(rupiah.Line).Value.Trim();
            return Field("Currency", marker, "IDR", .98m, rupiah.Page, rupiah.Line);
        }

        return new ExtractedField("Currency", null, "IDR", .80m, "default", null, null, ["Defaulted to IDR because no '$' currency marker was found"]);
    }

    private static ExtractedField ExtractSupplier(List<(int Page, string Line)> lines)
    {
        var labeled = FindLabel(lines, SupplierLabels);
        if (labeled is not null && !AddressRegex().IsMatch(labeled.Value.Value)) return Field("SupplierName", labeled.Value.Value, NormalizeSupplier(labeled.Value.Value), .90m, labeled.Value.Page, labeled.Value.Line);

        var candidate = lines.Take(12).FirstOrDefault(x => CompanyNameRegex().IsMatch(x.Line) && !AddressRegex().IsMatch(x.Line));
        if (!string.IsNullOrWhiteSpace(candidate.Line)) return Field("SupplierName", candidate.Line, NormalizeSupplier(CompanyNameRegex().Match(candidate.Line).Value), .85m, candidate.Page, candidate.Line, "Detected from company-name pattern");
        candidate = lines.Take(10).FirstOrDefault(x => x.Line.Length is >= 3 and <= 80 && x.Line.Count(char.IsLetter) >= 3 && !Regex.IsMatch(x.Line, @"(?i)invoice|faktur|bill(?:ed)?\s+to|alamat|address|telp|phone|tanggal|date|\bjl\.?|jalan"));
        return string.IsNullOrWhiteSpace(candidate.Line) ? Missing("SupplierName") : Field("SupplierName", candidate.Line, NormalizeSupplier(candidate.Line), .78m, candidate.Page, candidate.Line, "Fallback: first plausible header line");
    }

    private static ExtractedField ExtractTotal(List<(int Page, string Line)> lines)
    {
        foreach (var label in TotalLabels)
        {
            var candidates = lines.Where(x => x.Line.Contains(label, StringComparison.OrdinalIgnoreCase))
                .Select(x => (x.Page, x.Line, Match: MoneyRegex().Matches(x.Line).LastOrDefault()))
                .Where(x => x.Match is not null).ToList();
            if (candidates.Count == 0) continue;
            var hit = candidates.Last();
            var raw = hit.Match!.Value;
            return Field("TotalInvoice", raw, NormalizeMoney(raw), label == "total" ? .82m : .95m, hit.Page, hit.Line);
        }

        // Some invoice layouts stack the amount directly below a standalone
        // total label. Keep the lookup on the same page and close to the label
        // so a table column headed "Total" cannot consume a line-item amount.
        for (var index = 0; index < lines.Count; index++)
        {
            var label = TotalLabels.FirstOrDefault(candidate => IsStandaloneLabel(lines[index].Line, candidate));
            if (label is null) continue;
            for (var offset = 1; offset <= 2 && index + offset < lines.Count; offset++)
            {
                var valueLine = lines[index + offset];
                if (valueLine.Page != lines[index].Page) break;
                var money = MoneyRegex().Matches(valueLine.Line).LastOrDefault();
                if (money is null) continue;
                var raw = money.Value;
                var evidence = $"{lines[index].Line} -> {valueLine.Line}";
                return Field("TotalInvoice", raw, NormalizeMoney(raw), label == "total" ? .88m : .93m, valueLine.Page, evidence, "Detected from stacked label/value layout");
            }
        }
        return Missing("TotalInvoice");
    }

    private static bool IsStandaloneLabel(string line, string label) =>
        Regex.IsMatch(line, $@"(?i)^\s*{Regex.Escape(label)}\s*[:#-]?\s*$", RegexOptions.CultureInvariant);

    private static ExtractedField ExtractInvoiceNumber(List<(int Page, string Line)> lines)
    {
        var labeled = ExtractLabeled(lines, "InvoiceNo", InvoiceLabels, NormalizeIdentifier);
        if (labeled.NormalizedValue is not null) return labeled;
        if (lines.Any(x => InvoiceDocumentMarkerRegex().IsMatch(x.Line)))
        {
            foreach (var item in lines.Take(30))
            {
                var match = GenericNumberLabelRegex().Match(item.Line);
                if (!match.Success) continue;
                var raw = match.Groups["value"].Value;
                var normalized = NormalizeIdentifier(raw);
                if (normalized is not null)
                    return Field("InvoiceNo", raw, normalized, .88m, item.Page, item.Line, "Generic 'No' label interpreted in invoice context");
            }
        }
        foreach (var item in lines.Take(30))
        {
            var match = InvoiceNumberRegex().Match(item.Line);
            if (match.Success) return Field("InvoiceNo", match.Value, NormalizeIdentifier(match.Value), .70m, item.Page, item.Line, "Fallback: invoice-number pattern");
        }
        return labeled;
    }

    private static ExtractedField ExtractInvoiceDate(List<(int Page, string Line)> lines)
    {
        var hit = FindLabel(lines, DateLabels);
        if (hit is not null)
        {
            var raw = hit.Value.Value;
            var normalized = NormalizeDate(raw);
            if (normalized is null)
            {
                var date = DateValueRegex().Match(raw);
                if (date.Success)
                {
                    raw = date.Value;
                    normalized = NormalizeDate(raw);
                }
            }
            if (normalized is not null) return Field("InvoiceDate", raw, normalized, .92m, hit.Value.Page, hit.Value.Line);
        }
        foreach (var item in lines.Take(30))
        {
            var match = DateValueRegex().Match(item.Line);
            if (!match.Success) continue;
            var normalized = NormalizeDate(match.Value);
            if (normalized is not null) return Field("InvoiceDate", match.Value, normalized, .68m, item.Page, item.Line, "Fallback: date pattern without a recognized label");
        }
        return Missing("InvoiceDate");
    }

    private static ExtractedField ExtractLabeled(List<(int Page, string Line)> lines, string name, string[] labels, Func<string, string?> normalize)
    {
        var hit = FindLabel(lines, labels);
        if (hit is null) return Missing(name);
        var normalized = normalize(hit.Value.Value);
        return Field(name, hit.Value.Value, normalized, normalized is null ? .45m : .92m, hit.Value.Page, hit.Value.Line);
    }

    private static (int Page, string Line, string Value)? FindLabel(List<(int Page, string Line)> lines, string[] labels)
    {
        foreach (var item in lines)
        foreach (var label in labels.OrderByDescending(x => x.Length))
        {
            var match = Regex.Match(item.Line, $@"(?i)\b{Regex.Escape(label)}\b\s*[:#-]?\s*(?<value>.+)$");
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups["value"].Value))
                return (item.Page, item.Line, match.Groups["value"].Value.Trim());
        }
        return null;
    }

    private static IEnumerable<string> NormalizeLines(string text) => text.Replace('\u00a0', ' ').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(x => WhitespaceRegex().Replace(x, " ").Trim()).Where(x => x.Length > 1);
    private static string CleanText(string value) => Regex.Replace(value.Trim(' ', ':', '-', '#'), @"\s{2,}", " ");
    private static string NormalizeSupplier(string value) => CleanText(Regex.Split(value, @"(?i)\s+(?:invoice|faktur|bill(?:ed)?\s+to|tanggal|date|halaman|page)\b")[0]);
    private static string? NormalizeIdentifier(string value) { var m = Regex.Match(value, @"[A-Za-z0-9][A-Za-z0-9./_-]*"); return m.Success && m.Value.Any(char.IsDigit) ? m.Value.TrimEnd('.', '/', '-', '_').ToUpperInvariant() : null; }

    private static string? NormalizeDate(string value)
    {
        var cleaned = Regex.Replace(value, @"(?i)^(?:date|tanggal)\s*[:#-]?\s*", "").Trim();
        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "dd MMM yyyy", "d MMM yyyy", "dd MMMM yyyy", "d MMMM yyyy", "MMM dd, yyyy", "MMMM dd, yyyy"];
        return DateTime.TryParseExact(cleaned, formats, Indonesian, DateTimeStyles.AllowWhiteSpaces, out var date) || DateTime.TryParse(cleaned, Indonesian, DateTimeStyles.AllowWhiteSpaces, out date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    private static string? NormalizeMoney(string raw)
    {
        var value = Regex.Replace(raw, @"(?i)(rp|idr|usd|eur|sgd|aud|\$|€)", "").Trim().Replace(" ", "");
        if (value.Contains('.') && value.Contains(',')) value = value.LastIndexOf(',') > value.LastIndexOf('.') ? value.Replace(".", "").Replace(',', '.') : value.Replace(",", "");
        else if (value.Count(c => c == '.') > 1 || (value.Contains('.') && value.Split('.').Last().Length == 3)) value = value.Replace(".", "");
        else if (value.Count(c => c == ',') > 1 || (value.Contains(',') && value.Split(',').Last().Length == 3)) value = value.Replace(",", "");
        else value = value.Replace(',', '.');
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount.ToString("0.00", CultureInfo.InvariantCulture) : null;
    }

    private static ExtractedField Field(string name, string raw, string? normalized, decimal confidence, int page, string evidence, string? warning = null) => new(name, raw, normalized, confidence, "rules", page, evidence, warning is null ? [] : [warning]);
    private static ExtractedField Missing(string name) => new(name, null, null, 0m, "rules", null, null, ["Field not found"]);

    [GeneratedRegex(@"(?i)\b(PT\.?|CV\.?|UD\.?|Toko|Apotek|Bengkel|Studio|Freelancer)\s+[A-Z0-9][\p{L}0-9 .,&'-]{2,}")]
    private static partial Regex CompanyNameRegex();
    [GeneratedRegex(@"(?i)(?:Rp\.?|IDR|USD|EUR|SGD|AUD|\$|€)?\s*-?\d[\d.,]*(?:\s*(?:IDR|USD|EUR|SGD|AUD))?")]
    private static partial Regex MoneyRegex();
    [GeneratedRegex(@"(?i)(?:\bIDR\b|\bRp\.?\s*(?=\d))")]
    private static partial Regex RupiahMarkerRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"(?i)\b(?:jl\.?|jalan|street|road|alamat|address)\b")]
    private static partial Regex AddressRegex();
    [GeneratedRegex(@"(?i)\b(?:INV|INVOICE|FJ|FAK|NOTA)[-/# ]?[A-Z0-9][A-Z0-9./_-]*\d[A-Z0-9./_-]*\b")]
    private static partial Regex InvoiceNumberRegex();
    [GeneratedRegex(@"(?i)\b(?:invoice|faktur|nota)\b")]
    private static partial Regex InvoiceDocumentMarkerRegex();
    [GeneratedRegex(@"(?i)\bno\.?\s*[:#-]\s*(?<value>[A-Z0-9][A-Z0-9./_-]*)")]
    private static partial Regex GenericNumberLabelRegex();
    [GeneratedRegex(@"(?i)\b(?:\d{1,2}[-/]\d{1,2}[-/]\d{2,4}|\d{4}[-/]\d{1,2}[-/]\d{1,2}|\d{1,2}\s+(?:jan(?:uari|uary)?|feb(?:ruari|ruary)?|mar(?:et|ch)?|apr(?:il)?|mei|may|jun(?:i|e)?|jul(?:i|y)?|agu(?:stus)?|aug(?:ust)?|sep(?:tember)?|okt(?:ober)?|oct(?:ober)?|nov(?:ember)?|des(?:ember)?|dec(?:ember)?)\s+\d{4})\b")]
    private static partial Regex DateValueRegex();
}
