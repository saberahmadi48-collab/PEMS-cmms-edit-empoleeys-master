// Mirrors PEMS.Application/DTOs/ExecutiveDashboardDtos.cs (camelCase JSON from ExecutiveDashboardController.Data).
export interface Count { label: string; value: number }
export interface ProjectOption { id: number; name: string; code?: string | null; documents: number }
export interface Bucket { key: "Approved" | "InReview" | "Returned" | "Draft" | "Other"; value: number }
export interface DisciplineRow { name: string; total: number; approved: number; inReview: number; returned: number; draft: number; approvedPercent: number }
export interface ProjectRow { projectId: number; name: string; total: number; approved: number; approvedPercent: number; othersCount: number }
export interface MonthPoint { date: string; issued: number; cumulative: number }
export interface RevisionPressure { documentId: number; documentNo: string; title: string; status: string; revisions: number }
export interface Completeness { withDisciplinePercent: number; withPlannedDatePercent: number; withClientResponsePercent: number; withIssuedRevisionPercent: number }

export interface Engineering {
  total: number; projectCount: number; approved: number; inReview: number; returned: number; draft: number; other: number; approvedPercent: number;
  rejectedTrue: number; returnedWithComments: number; overdue: number;
  totalRevisions: number; revisionsPerDocument: number; issuedRevisions: number;
  buckets: Bucket[]; disciplines: DisciplineRow[]; issuance: MonthPoint[]; clientResponses: Count[];
  projects: ProjectRow[]; revisionPressure: RevisionPressure[]; completeness: Completeness;
}
export interface Vdr {
  total: number; closed: number; underReview: number; pendingResubmission: number; overdue: number; submissions: number;
  byWorkflow: Count[]; byVendor: Count[];
}
export interface CurrencyValue { currency: string; value: number; count: number }
export interface Procurement {
  packages: number; tenders: number; bids: number; contracts: number; purchaseOrders: number; ntpsIssued: number;
  materialRequisitions: number; openMaterialRequisitions: number; mrByStatus: Count[]; purchaseOrderValue: CurrencyValue[];
}
export interface Transmittals { total: number; sent: number; byMonth: MonthPoint[] }
export interface Operations { totalWorkOrders: number; openWorkOrders: number; criticalWorkOrders: number; avgCompletionDays: number | null; equipment: number; byPriority: Count[] }
export interface Correspondence { total: number; incoming: number; outgoing: number; overdueReferrals: number; overdueResponses: number }
export interface Attention { key: string; severity: "High" | "Medium" | "Info"; count: number; module: string }

export interface DashboardData {
  generatedAt: string;
  projectId: number | null;
  projects: ProjectOption[];
  engineering: Engineering | null;
  vdr: Vdr | null;
  procurement: Procurement | null;
  transmittals: Transmittals | null;
  operations: Operations | null;
  correspondence: Correspondence | null;
  attention: Attention[];
  failedSections: string[];
}

export interface Boot {
  dataUrl: string;
  links: Record<string, string | null>;
  i18n: Record<string, string>;
  locale: string;
  rtl: boolean;
}
