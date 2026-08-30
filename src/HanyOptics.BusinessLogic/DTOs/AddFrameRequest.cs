using System.ComponentModel.DataAnnotations;
using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;

// One frame being entered into stock, normally starting from a scan of its printed label.
public class AddFrameRequest
{
    [Required(ErrorMessage = "امسح الباركود أو اكتبه")]
    [StringLength(50)]
    public string Barcode { get; set; } = string.Empty;

    public FrameTrackingType TrackingType { get; set; } = FrameTrackingType.Individual;
    public FrameCategory Category { get; set; } = FrameCategory.Optical;

    [Required(ErrorMessage = "أدخل الماركة")]
    [StringLength(100)]
    public string? Brand { get; set; }

    [StringLength(100)]
    public string? ModelName { get; set; }

    [StringLength(50)]
    public string? Color { get; set; }

    [StringLength(20)]
    public string? Size { get; set; }

    [Range(0, 999999, ErrorMessage = "التكلفة غير صحيحة")]
    public decimal CostPrice { get; set; }

    [Range(0, 999999, ErrorMessage = "سعر البيع غير صحيح")]
    public decimal SellPrice { get; set; }

    // Ignored for an individual frame, which is a single physical piece by definition -
    // the service forces it to 1 rather than trusting whatever the form posted.
    [Range(1, 10000, ErrorMessage = "الكمية غير صحيحة")]
    public int Quantity { get; set; } = 1;

    [StringLength(500)]
    public string? Notes { get; set; }
}

// What the screen learns when a barcode is scanned, before anything is saved.
//
// Worth being clear about how little a barcode can say on its own: the codes this shop
// prints carry a price and nothing else - no brand, no model, no colour - so a barcode
// nobody has seen before cannot fill the form in. The rest of the fields can only be
// filled from a frame already in the database, which is what Existing* below carries.
public class FrameBarcodeLookupResult
{
    // Already in stock: the scan is telling the user this frame is known, not new.
    public bool AlreadyExists { get; set; }
    public int ExistingFrameId { get; set; }
    public string? ExistingLabel { get; set; }
    public int ExistingQtyAvailable { get; set; }
    public string? ExistingStatus { get; set; }

    // The stored frame, field by field, so a known barcode fills the whole form rather
    // than just announcing itself.
    public string? ExistingBrand { get; set; }
    public string? ExistingModelName { get; set; }
    public string? ExistingColor { get; set; }
    public string? ExistingSize { get; set; }
    public string? ExistingCategory { get; set; }
    public string? ExistingTrackingType { get; set; }
    public decimal ExistingCostPrice { get; set; }
    public decimal ExistingSellPrice { get; set; }
    public string? ExistingNotes { get; set; }

    // Pulled out of the barcode itself when it follows sp_generate_barcode's shape, so the
    // sell price does not have to be retyped from the label the scanner just read.
    public decimal? DecodedSellPrice { get; set; }

    public string? Message { get; set; }
}

public class AddFrameOutcome
{
    public bool Succeeded { get; init; }
    public int FrameId { get; init; }
    public string? ErrorMessage { get; init; }

    public static AddFrameOutcome Success(int frameId) => new() { Succeeded = true, FrameId = frameId };
    public static AddFrameOutcome Failure(string message) => new() { ErrorMessage = message };
}
