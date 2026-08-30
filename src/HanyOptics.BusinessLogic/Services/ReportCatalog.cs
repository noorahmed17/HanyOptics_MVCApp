using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Services;

// Every report the admin screen offers, declared in one place.
//
// Most of them sit on the reporting views the database already ships (vw_daily_sales,
// vw_profit_monthly, ...), which is deliberate: those views already handle the awkward
// parts - refunds counted negative, cancelled orders excluded, payment date and delivery
// date being different days - and re-deriving that here would be a second version of the
// same rules, free to drift from the first. The handful of reports with no view behind
// them are aggregates over plain tables and are written out below.
//
// No stored procedures are involved. Every query is read-only.
internal static class ReportCatalog
{
    // Reports filtered by date all bind the same two parameters, and every query declares
    // them whether it uses them or not, so execution never has to special-case a report.
    // The upper bound is inclusive of the whole day: a datetime column is compared against
    // the *next* midnight, so an order placed at 14:30 on the last day still counts.
    public static readonly IReadOnlyList<ReportDefinition> All =
    [
        // ─────────────────────────── فلوس ومبيعات ───────────────────────────
        new ReportDefinition
        {
            Key = "daily-sales",
            Title = "يومية المبيعات",
            Description = "المحصّل كل يوم كاش وفيزا، وعدد الطلبات المسلّمة وقيمتها. المردودات متخصومة.",
            Icon = "💵",
            Group = ReportGroup.Money,
            Sql = """
                  SELECT sale_date, cash_collected, visa_collected, total_collected,
                         deliveries_count, deliveries_total
                  FROM vw_daily_sales
                  WHERE (@from IS NULL OR sale_date >= @from)
                    AND (@to   IS NULL OR sale_date <= @to)
                  ORDER BY sale_date DESC
                  """,
            Columns =
            [
                new() { Key = "sale_date",        Label = "التاريخ",           Type = ReportColumnType.Date },
                new() { Key = "cash_collected",   Label = "كاش",               Type = ReportColumnType.Money, SignedMoney = true },
                new() { Key = "visa_collected",   Label = "فيزا",              Type = ReportColumnType.Money, SignedMoney = true },
                new() { Key = "total_collected",  Label = "إجمالي المحصّل",     Type = ReportColumnType.Money, SignedMoney = true },
                new() { Key = "deliveries_count", Label = "عدد التسليمات",      Type = ReportColumnType.Number },
                new() { Key = "deliveries_total", Label = "قيمة التسليمات",     Type = ReportColumnType.Money }
            ],
            Kpis =
            [
                new() { Label = "إجمالي المحصّل", Column = "total_collected" },
                new() { Label = "كاش",           Column = "cash_collected" },
                new() { Label = "فيزا",          Column = "visa_collected" },
                new() { Label = "عدد التسليمات",  Column = "deliveries_count", Format = ReportColumnType.Number }
            ]
        },

        new ReportDefinition
        {
            Key = "monthly-profit",
            Title = "الأرباح الشهرية",
            Description = "الإيراد والتكلفة والربح لكل شهر — الصورة الكبيرة لأداء المحل.",
            Icon = "📈",
            Group = ReportGroup.Money,
            // Aggregated by month already; a day-level filter would only chop a month in half.
            SupportsDateRange = false,
            Sql = """
                  SELECT sale_year, sale_month, total_orders, total_revenue, total_cost, total_profit
                  FROM vw_profit_monthly
                  WHERE (@from IS NULL OR @from IS NOT NULL)
                    AND (@to   IS NULL OR @to   IS NOT NULL)
                  ORDER BY sale_year DESC, sale_month DESC
                  """,
            Columns =
            [
                new() { Key = "sale_year",     Label = "السنة",        Type = ReportColumnType.Number },
                new() { Key = "sale_month",    Label = "الشهر",        Type = ReportColumnType.Number },
                new() { Key = "total_orders",  Label = "عدد الطلبات",   Type = ReportColumnType.Number },
                new() { Key = "total_revenue", Label = "الإيراد",       Type = ReportColumnType.Money },
                new() { Key = "total_cost",    Label = "التكلفة",       Type = ReportColumnType.Money },
                new() { Key = "total_profit",  Label = "الربح",         Type = ReportColumnType.Money, SignedMoney = true }
            ],
            Kpis =
            [
                new() { Label = "إجمالي الإيراد", Column = "total_revenue" },
                new() { Label = "إجمالي التكلفة", Column = "total_cost" },
                new() { Label = "إجمالي الربح",   Column = "total_profit" }
            ]
        },

        new ReportDefinition
        {
            Key = "item-profit",
            Title = "الربح لكل بند",
            Description = "ربح الإطار والعدسة في كل بند على حدة — بيوري البنود اللي بتكسب واللي بتخسر.",
            Icon = "🧾",
            Group = ReportGroup.Money,
            Sql = """
                  SELECT sale_date, invoice_number, customer_name, item_type,
                         frame_revenue, frame_cost, frame_profit,
                         lens_revenue, lens_cost, lens_profit,
                         total_revenue, total_profit
                  FROM vw_profit_per_item
                  WHERE (@from IS NULL OR sale_date >= @from)
                    AND (@to   IS NULL OR sale_date <= @to)
                  ORDER BY sale_date DESC, item_id DESC
                  """,
            Columns =
            [
                new() { Key = "sale_date",      Label = "التاريخ",       Type = ReportColumnType.Date },
                new() { Key = "invoice_number", Label = "رقم الفاتورة" },
                new() { Key = "customer_name",  Label = "العميل" },
                new() { Key = "item_type",      Label = "نوع البند" },
                new() { Key = "frame_revenue",  Label = "إيراد الإطار",   Type = ReportColumnType.Money },
                new() { Key = "frame_cost",     Label = "تكلفة الإطار",   Type = ReportColumnType.Money },
                new() { Key = "frame_profit",   Label = "ربح الإطار",     Type = ReportColumnType.Money, SignedMoney = true },
                new() { Key = "lens_revenue",   Label = "إيراد العدسة",   Type = ReportColumnType.Money },
                new() { Key = "lens_cost",      Label = "تكلفة العدسة",   Type = ReportColumnType.Money },
                new() { Key = "lens_profit",    Label = "ربح العدسة",     Type = ReportColumnType.Money, SignedMoney = true },
                new() { Key = "total_revenue",  Label = "إجمالي الإيراد", Type = ReportColumnType.Money },
                new() { Key = "total_profit",   Label = "إجمالي الربح",   Type = ReportColumnType.Money, SignedMoney = true }
            ],
            Kpis =
            [
                new() { Label = "عدد البنود",    Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number },
                new() { Label = "إجمالي الإيراد", Column = "total_revenue" },
                new() { Label = "إجمالي الربح",   Column = "total_profit" },
                new() { Label = "متوسط ربح البند", Column = "total_profit", Aggregate = ReportAggregate.Average }
            ]
        },

        new ReportDefinition
        {
            Key = "staff-sales",
            Title = "أداء الموظفين",
            Description = "كل موظف: عدد الطلبات اللي عملها وقيمتها، والدفعات اللي استلمها.",
            Icon = "👤",
            Group = ReportGroup.Money,
            // Two independent aggregates - a staff member can take a payment on an order
            // someone else opened - so they are counted separately and joined onto the user.
            Sql = """
                  SELECT u.name AS staff_name,
                         u.role AS staff_role,
                         ISNULL(o.orders_count,   0) AS orders_count,
                         ISNULL(o.orders_total,   0) AS orders_total,
                         ISNULL(p.payments_count, 0) AS payments_count,
                         ISNULL(p.payments_net,   0) AS payments_net
                  FROM users u
                  LEFT JOIN (
                      SELECT created_by, COUNT(*) AS orders_count, SUM(total_amount) AS orders_total
                      FROM orders
                      WHERE status <> 'cancelled'
                        AND (@from IS NULL OR order_date >= @from)
                        AND (@to   IS NULL OR order_date <  DATEADD(day, 1, @to))
                      GROUP BY created_by
                  ) o ON o.created_by = u.user_id
                  LEFT JOIN (
                      SELECT received_by,
                             COUNT(*) AS payments_count,
                             SUM(CASE WHEN payment_type = 'refund' THEN -amount ELSE amount END) AS payments_net
                      FROM payments
                      WHERE (@from IS NULL OR paid_at >= @from)
                        AND (@to   IS NULL OR paid_at <  DATEADD(day, 1, @to))
                      GROUP BY received_by
                  ) p ON p.received_by = u.user_id
                  WHERE u.is_active = 1
                  ORDER BY ISNULL(o.orders_total, 0) DESC
                  """,
            Columns =
            [
                new() { Key = "staff_name",     Label = "الموظف" },
                new() { Key = "staff_role",     Label = "الصلاحية" },
                new() { Key = "orders_count",   Label = "عدد الطلبات",     Type = ReportColumnType.Number },
                new() { Key = "orders_total",   Label = "قيمة الطلبات",    Type = ReportColumnType.Money },
                new() { Key = "payments_count", Label = "عدد الدفعات",     Type = ReportColumnType.Number },
                new() { Key = "payments_net",   Label = "صافي المحصّل",     Type = ReportColumnType.Money, SignedMoney = true }
            ],
            Kpis =
            [
                new() { Label = "إجمالي الطلبات", Column = "orders_count",  Format = ReportColumnType.Number },
                new() { Label = "قيمة الطلبات",   Column = "orders_total" },
                new() { Label = "صافي المحصّل",    Column = "payments_net" }
            ]
        },

        // ─────────────────────────── مستحقات ───────────────────────────
        new ReportDefinition
        {
            Key = "outstanding",
            Title = "المبالغ المستحقة",
            Description = "مين لسه عليه فلوس وكام — مرتّبين بالأكبر. ده الفلوس اللي بره المحل دلوقتي.",
            Icon = "⏳",
            Group = ReportGroup.Receivables,
            // A debt is owed now, not "during March" - filtering by date would hide debts
            // whose order happens to be older than the range and make the total look better
            // than it is.
            SupportsDateRange = false,
            Sql = """
                  SELECT order_id, invoice_number, customer_name, customer_phone,
                         order_date, status, total_amount, paid_amount, remaining_amount
                  FROM vw_pending_payments
                  WHERE (@from IS NULL OR @from IS NOT NULL)
                    AND (@to   IS NULL OR @to   IS NOT NULL)
                  ORDER BY remaining_amount DESC
                  """,
            Columns =
            [
                new() { Key = "invoice_number",   Label = "رقم الفاتورة" },
                new() { Key = "customer_name",    Label = "العميل" },
                new() { Key = "customer_phone",   Label = "التليفون" },
                new() { Key = "order_date",       Label = "تاريخ الطلب",  Type = ReportColumnType.DateTime },
                new() { Key = "status",           Label = "الحالة" },
                new() { Key = "total_amount",     Label = "الإجمالي",     Type = ReportColumnType.Money },
                new() { Key = "paid_amount",      Label = "المدفوع",      Type = ReportColumnType.Money },
                new() { Key = "remaining_amount", Label = "المتبقي",      Type = ReportColumnType.Money }
            ],
            Kpis =
            [
                new() { Label = "عدد الطلبات",      Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number },
                new() { Label = "إجمالي المستحق",   Column = "remaining_amount" },
                new() { Label = "متوسط المستحق",    Column = "remaining_amount", Aggregate = ReportAggregate.Average }
            ]
        },

        new ReportDefinition
        {
            Key = "payments-log",
            Title = "سجل الدفعات والمردودات",
            Description = "كل دفعة واسترداد: امتى، على أنهي فاتورة، كاش ولا فيزا، ومين استلمها.",
            Icon = "🧮",
            Group = ReportGroup.Receivables,
            Sql = """
                  SELECT p.paid_at, o.invoice_number, o.customer_name,
                         p.payment_type, p.payment_method,
                         CASE WHEN p.payment_type = 'refund' THEN -p.amount ELSE p.amount END AS amount_signed,
                         ISNULL(u.name, N'—') AS received_by
                  FROM payments p
                  JOIN orders o ON o.order_id = p.order_id
                  LEFT JOIN users u ON u.user_id = p.received_by
                  WHERE (@from IS NULL OR p.paid_at >= @from)
                    AND (@to   IS NULL OR p.paid_at <  DATEADD(day, 1, @to))
                  ORDER BY p.paid_at DESC
                  """,
            Columns =
            [
                new() { Key = "paid_at",        Label = "التاريخ",     Type = ReportColumnType.DateTime },
                new() { Key = "invoice_number", Label = "رقم الفاتورة" },
                new() { Key = "customer_name",  Label = "العميل" },
                new() { Key = "payment_type",   Label = "النوع" },
                new() { Key = "payment_method", Label = "الطريقة" },
                new() { Key = "amount_signed",  Label = "المبلغ",      Type = ReportColumnType.Money, SignedMoney = true },
                new() { Key = "received_by",    Label = "استلمها" }
            ],
            Kpis =
            [
                new() { Label = "عدد الحركات", Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number },
                new() { Label = "الصافي",      Column = "amount_signed" }
            ]
        },

        // ─────────────────────────── مخزون ───────────────────────────
        new ReportDefinition
        {
            Key = "frame-stock",
            Title = "جرد الإطارات وقيمته",
            Description = "الإطارات المتاحة دلوقتي وقيمتها بالتكلفة وبالبيع — الفلوس النايمة في المخزن.",
            Icon = "📦",
            Group = ReportGroup.Stock,
            SupportsDateRange = false,
            Sql = """
                  SELECT barcode, brand, model_name, color, size, category, tracking_type,
                         cost_price, sell_price, qty_available,
                         cost_price * qty_available AS stock_cost,
                         sell_price * qty_available AS stock_value
                  FROM vw_frame_inventory
                  WHERE (@from IS NULL OR @from IS NOT NULL)
                    AND (@to   IS NULL OR @to   IS NOT NULL)
                  ORDER BY sell_price * qty_available DESC
                  """,
            Columns =
            [
                new() { Key = "barcode",       Label = "الباركود" },
                new() { Key = "brand",         Label = "الماركة" },
                new() { Key = "model_name",    Label = "الموديل" },
                new() { Key = "color",         Label = "اللون" },
                new() { Key = "size",          Label = "المقاس" },
                new() { Key = "category",      Label = "النوع" },
                new() { Key = "tracking_type", Label = "التتبع" },
                new() { Key = "cost_price",    Label = "التكلفة",       Type = ReportColumnType.Money },
                new() { Key = "sell_price",    Label = "سعر البيع",     Type = ReportColumnType.Money },
                new() { Key = "qty_available", Label = "المتاح",        Type = ReportColumnType.Number },
                new() { Key = "stock_cost",    Label = "قيمة بالتكلفة", Type = ReportColumnType.Money },
                new() { Key = "stock_value",   Label = "قيمة بالبيع",   Type = ReportColumnType.Money }
            ],
            Kpis =
            [
                new() { Label = "عدد الأصناف",     Aggregate = ReportAggregate.Count,   Format = ReportColumnType.Number },
                new() { Label = "القطع المتاحة",   Column = "qty_available",            Format = ReportColumnType.Number },
                new() { Label = "القيمة بالتكلفة", Column = "stock_cost" },
                new() { Label = "القيمة بالبيع",   Column = "stock_value" }
            ]
        },

        new ReportDefinition
        {
            Key = "lens-stock",
            Title = "مخزون العدسات",
            Description = "أنواع العدسات المتاحة وكمياتها وقيمتها.",
            Icon = "🔍",
            Group = ReportGroup.Stock,
            SupportsDateRange = false,
            Sql = """
                  SELECT lens_type, material, coating, sphere_range,
                         cost_price, sell_price, qty_available,
                         cost_price * qty_available AS stock_cost,
                         sell_price * qty_available AS stock_value
                  FROM lens_stock
                  WHERE (@from IS NULL OR @from IS NOT NULL)
                    AND (@to   IS NULL OR @to   IS NOT NULL)
                  ORDER BY sell_price * qty_available DESC
                  """,
            Columns =
            [
                new() { Key = "lens_type",     Label = "نوع العدسة" },
                new() { Key = "material",      Label = "الخامة" },
                new() { Key = "coating",       Label = "الطبقة" },
                new() { Key = "sphere_range",  Label = "المدى" },
                new() { Key = "cost_price",    Label = "التكلفة",       Type = ReportColumnType.Money },
                new() { Key = "sell_price",    Label = "سعر البيع",     Type = ReportColumnType.Money },
                new() { Key = "qty_available", Label = "المتاح",        Type = ReportColumnType.Number },
                new() { Key = "stock_cost",    Label = "قيمة بالتكلفة", Type = ReportColumnType.Money },
                new() { Key = "stock_value",   Label = "قيمة بالبيع",   Type = ReportColumnType.Money }
            ],
            Kpis =
            [
                new() { Label = "عدد الأصناف",     Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number },
                new() { Label = "القطع المتاحة",   Column = "qty_available",          Format = ReportColumnType.Number },
                new() { Label = "القيمة بالتكلفة", Column = "stock_cost" },
                new() { Label = "القيمة بالبيع",   Column = "stock_value" }
            ]
        },

        new ReportDefinition
        {
            Key = "damage-losses",
            Title = "الهالك والتالف",
            Description = "الإطارات اللي اتشالت من المخزون واتكلفتها — خسارة مباشرة.",
            Icon = "💔",
            Group = ReportGroup.Stock,
            Sql = """
                  SELECT damage_date, barcode, brand, model_name, tracking_type,
                         qty_damaged, unit_cost, cost_loss, recorded_by, notes
                  FROM vw_frame_damage_losses
                  WHERE (@from IS NULL OR damage_date >= @from)
                    AND (@to   IS NULL OR damage_date <= @to)
                  ORDER BY damage_date DESC
                  """,
            Columns =
            [
                new() { Key = "damage_date",   Label = "التاريخ",   Type = ReportColumnType.Date },
                new() { Key = "barcode",       Label = "الباركود" },
                new() { Key = "brand",         Label = "الماركة" },
                new() { Key = "model_name",    Label = "الموديل" },
                new() { Key = "tracking_type", Label = "التتبع" },
                new() { Key = "qty_damaged",   Label = "الكمية",    Type = ReportColumnType.Number },
                new() { Key = "unit_cost",     Label = "تكلفة القطعة", Type = ReportColumnType.Money },
                new() { Key = "cost_loss",     Label = "الخسارة",   Type = ReportColumnType.Money },
                new() { Key = "recorded_by",   Label = "سجّلها" },
                new() { Key = "notes",         Label = "ملاحظات" }
            ],
            Kpis =
            [
                new() { Label = "عدد الحالات",   Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number },
                new() { Label = "القطع التالفة", Column = "qty_damaged",            Format = ReportColumnType.Number },
                new() { Label = "إجمالي الخسارة", Column = "cost_loss" }
            ]
        },

        new ReportDefinition
        {
            Key = "top-brands",
            Title = "أكثر الماركات مبيعاً",
            Description = "الماركات مرتّبة بالإيراد — بتقول تشتري إيه تاني وتسيب إيه.",
            Icon = "🏆",
            Group = ReportGroup.Stock,
            Sql = """
                  SELECT f.brand,
                         COUNT(*) AS items_sold,
                         SUM(oi.frame_agreed_price) AS revenue,
                         SUM(f.cost_price)          AS cost,
                         SUM(oi.frame_agreed_price - f.cost_price) AS profit
                  FROM order_items oi
                  JOIN frames f ON f.frame_id = oi.frame_id
                  JOIN orders o ON o.order_id = oi.order_id
                  WHERE oi.status = 'active'
                    AND o.status <> 'cancelled'
                    AND (@from IS NULL OR o.order_date >= @from)
                    AND (@to   IS NULL OR o.order_date <  DATEADD(day, 1, @to))
                  GROUP BY f.brand
                  ORDER BY SUM(oi.frame_agreed_price) DESC
                  """,
            Columns =
            [
                new() { Key = "brand",      Label = "الماركة" },
                new() { Key = "items_sold", Label = "عدد المبيعات", Type = ReportColumnType.Number },
                new() { Key = "revenue",    Label = "الإيراد",      Type = ReportColumnType.Money },
                new() { Key = "cost",       Label = "التكلفة",      Type = ReportColumnType.Money },
                new() { Key = "profit",     Label = "الربح",        Type = ReportColumnType.Money, SignedMoney = true }
            ],
            Kpis =
            [
                new() { Label = "عدد الماركات", Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number },
                new() { Label = "إجمالي الإيراد", Column = "revenue" },
                new() { Label = "إجمالي الربح",   Column = "profit" }
            ]
        },

        // ─────────────────────────── عمليات ───────────────────────────
        new ReportDefinition
        {
            Key = "orders-summary",
            Title = "ملخص الطلبات",
            Description = "كل الطلبات بحالتها وقيمتها ومين عملها.",
            Icon = "📋",
            Group = ReportGroup.Operations,
            Sql = """
                  SELECT invoice_number, customer_name, customer_phone, order_date, status,
                         delivery_type, total_amount, paid_amount, remaining_amount,
                         delivered_at, created_by
                  FROM vw_order_summary
                  WHERE (@from IS NULL OR order_date >= @from)
                    AND (@to   IS NULL OR order_date <  DATEADD(day, 1, @to))
                  ORDER BY order_date DESC
                  """,
            Columns =
            [
                new() { Key = "invoice_number",   Label = "رقم الفاتورة" },
                new() { Key = "customer_name",    Label = "العميل" },
                new() { Key = "customer_phone",   Label = "التليفون" },
                new() { Key = "order_date",       Label = "التاريخ",   Type = ReportColumnType.DateTime },
                new() { Key = "status",           Label = "الحالة" },
                new() { Key = "delivery_type",    Label = "التسليم" },
                new() { Key = "total_amount",     Label = "الإجمالي",  Type = ReportColumnType.Money },
                new() { Key = "paid_amount",      Label = "المدفوع",   Type = ReportColumnType.Money },
                new() { Key = "remaining_amount", Label = "المتبقي",   Type = ReportColumnType.Money },
                new() { Key = "delivered_at",     Label = "اتسلّم في",  Type = ReportColumnType.DateTime },
                new() { Key = "created_by",       Label = "عملها" }
            ],
            Kpis =
            [
                new() { Label = "عدد الطلبات",  Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number },
                new() { Label = "إجمالي القيمة", Column = "total_amount" },
                new() { Label = "المحصّل",       Column = "paid_amount" },
                new() { Label = "المتبقي",       Column = "remaining_amount" }
            ]
        },

        new ReportDefinition
        {
            Key = "frame-swaps",
            Title = "تبديل الإطارات",
            Description = "كل حالة تبديل إطار، وربحها أو خسارتها بعد التبديل.",
            Icon = "🔄",
            Group = ReportGroup.Operations,
            Sql = """
                  SELECT sale_date, invoice_number, customer_name, customer_phone,
                         frame_barcode, frame_brand, frame_model,
                         price_charged, frame_cost, profit_loss, discount_reason, created_by
                  FROM vw_frame_swaps
                  WHERE (@from IS NULL OR sale_date >= @from)
                    AND (@to   IS NULL OR sale_date <= @to)
                  ORDER BY sale_date DESC
                  """,
            Columns =
            [
                new() { Key = "sale_date",       Label = "التاريخ",     Type = ReportColumnType.Date },
                new() { Key = "invoice_number",  Label = "رقم الفاتورة" },
                new() { Key = "customer_name",   Label = "العميل" },
                new() { Key = "customer_phone",  Label = "التليفون" },
                new() { Key = "frame_barcode",   Label = "باركود الإطار" },
                new() { Key = "frame_brand",     Label = "الماركة" },
                new() { Key = "frame_model",     Label = "الموديل" },
                new() { Key = "price_charged",   Label = "المحصّل",      Type = ReportColumnType.Money },
                new() { Key = "frame_cost",      Label = "التكلفة",     Type = ReportColumnType.Money },
                new() { Key = "profit_loss",     Label = "ربح / خسارة", Type = ReportColumnType.Money, SignedMoney = true },
                new() { Key = "discount_reason", Label = "السبب" },
                new() { Key = "created_by",      Label = "عملها" }
            ],
            Kpis =
            [
                new() { Label = "عدد التبديلات", Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number },
                new() { Label = "صافي الربح",    Column = "profit_loss" }
            ]
        },

        new ReportDefinition
        {
            Key = "doctors",
            Title = "الأطباء والتحويلات",
            Description = "كل دكتور جاب كام طلب وبكام — مين مصدر الشغل الحقيقي.",
            Icon = "🩺",
            Group = ReportGroup.Operations,
            Sql = """
                  SELECT d.name AS doctor_name,
                         ISNULL(d.clinic, N'—') AS clinic,
                         ISNULL(d.phone,  N'—') AS phone,
                         COUNT(o.order_id) AS orders_count,
                         ISNULL(SUM(o.total_amount), 0) AS orders_total
                  FROM doctors d
                  LEFT JOIN orders o
                         ON o.doctor_id = d.doctor_id
                        AND o.status <> 'cancelled'
                        AND (@from IS NULL OR o.order_date >= @from)
                        AND (@to   IS NULL OR o.order_date <  DATEADD(day, 1, @to))
                  GROUP BY d.name, d.clinic, d.phone
                  ORDER BY COUNT(o.order_id) DESC, ISNULL(SUM(o.total_amount), 0) DESC
                  """,
            Columns =
            [
                new() { Key = "doctor_name",  Label = "الدكتور" },
                new() { Key = "clinic",       Label = "العيادة" },
                new() { Key = "phone",        Label = "التليفون" },
                new() { Key = "orders_count", Label = "عدد الطلبات", Type = ReportColumnType.Number },
                new() { Key = "orders_total", Label = "قيمة الطلبات", Type = ReportColumnType.Money }
            ],
            Kpis =
            [
                new() { Label = "عدد الأطباء",  Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number },
                new() { Label = "إجمالي الطلبات", Column = "orders_count", Format = ReportColumnType.Number },
                new() { Label = "إجمالي القيمة",  Column = "orders_total" }
            ]
        },

        new ReportDefinition
        {
            Key = "top-customers",
            Title = "أكثر العملاء شراءً",
            Description = "العملاء مرتّبين باللي صرفوه، وآخر مرة اشتروا فيها، واللي لسه عليهم.",
            Icon = "👥",
            Group = ReportGroup.Operations,
            Sql = """
                  SELECT c.name AS customer_name,
                         ISNULL(c.phone, N'—') AS phone,
                         COUNT(o.order_id) AS orders_count,
                         SUM(o.total_amount)     AS total_spent,
                         SUM(o.remaining_amount) AS still_owed,
                         MAX(o.order_date)       AS last_order
                  FROM customers c
                  JOIN orders o
                    ON o.customer_id = c.customer_id
                   AND o.status <> 'cancelled'
                   AND (@from IS NULL OR o.order_date >= @from)
                   AND (@to   IS NULL OR o.order_date <  DATEADD(day, 1, @to))
                  GROUP BY c.name, c.phone
                  ORDER BY SUM(o.total_amount) DESC
                  """,
            Columns =
            [
                new() { Key = "customer_name", Label = "العميل" },
                new() { Key = "phone",         Label = "التليفون" },
                new() { Key = "orders_count",  Label = "عدد الطلبات", Type = ReportColumnType.Number },
                new() { Key = "total_spent",   Label = "إجمالي الشراء", Type = ReportColumnType.Money },
                new() { Key = "still_owed",    Label = "لسه عليه",     Type = ReportColumnType.Money },
                new() { Key = "last_order",    Label = "آخر طلب",      Type = ReportColumnType.DateTime }
            ],
            Kpis =
            [
                new() { Label = "عدد العملاء",   Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number },
                new() { Label = "إجمالي الشراء", Column = "total_spent" },
                new() { Label = "إجمالي المستحق", Column = "still_owed" }
            ]
        },

        new ReportDefinition
        {
            Key = "customer-history",
            Title = "سجل العملاء بالتفصيل",
            Description = "كل بند اشتراه كل عميل: الإطار، وصف العدسة، والدكتور — للرجوع لما عميل يسأل.",
            Icon = "🗂️",
            Group = ReportGroup.Operations,
            Sql = """
                  SELECT customer_name, phone, invoice_number, order_date, status,
                         item_type, frame_brand, frame_model, frame_barcode,
                         lens_description, doctor_name, total_amount, remaining_amount
                  FROM vw_customer_history
                  WHERE (@from IS NULL OR order_date >= @from)
                    AND (@to   IS NULL OR order_date <  DATEADD(day, 1, @to))
                  ORDER BY order_date DESC
                  """,
            Columns =
            [
                new() { Key = "customer_name",    Label = "العميل" },
                new() { Key = "phone",            Label = "التليفون" },
                new() { Key = "invoice_number",   Label = "رقم الفاتورة" },
                new() { Key = "order_date",       Label = "التاريخ",   Type = ReportColumnType.DateTime },
                new() { Key = "status",           Label = "الحالة" },
                new() { Key = "item_type",        Label = "نوع البند" },
                new() { Key = "frame_brand",      Label = "الماركة" },
                new() { Key = "frame_model",      Label = "الموديل" },
                new() { Key = "frame_barcode",    Label = "الباركود" },
                new() { Key = "lens_description", Label = "العدسة" },
                new() { Key = "doctor_name",      Label = "الدكتور" },
                new() { Key = "total_amount",     Label = "الإجمالي",  Type = ReportColumnType.Money },
                new() { Key = "remaining_amount", Label = "المتبقي",   Type = ReportColumnType.Money }
            ],
            Kpis =
            [
                new() { Label = "عدد البنود", Aggregate = ReportAggregate.Count, Format = ReportColumnType.Number }
            ]
        }
    ];

    public static ReportDefinition? Find(string? key) =>
        All.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
}
