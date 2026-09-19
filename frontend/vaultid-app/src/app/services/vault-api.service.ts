import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  ActivityEntry, Agreement, Category, CategoryAgreementView, CategoryView, ConsentMethod, FieldDefinition, FieldType,
  GeneratedShareCode, GenerateShareCodeRequest, Grant, Organisation, OrganisationShareRequest, PendingShareRequest,
  RegisterOrganisationRequest, SetCategoryAgreementRequest, ShareCode, ShareCodeRedemptionView, ShareDuration,
  ShareRequest, UpdateCategoryFieldsRequest, UpdateOrganisationComplianceRequest, UpdateOrganisationProfileRequest,
  VaultSummary,
} from '../models';

/**
 * The ONLY place the frontend talks to the backend. Every method is a thin
 * HTTP call that returns exactly what the API returns. There is deliberately no
 * decision-making, validation, permission logic or event handling here - all of
 * that lives in the backend. Components render whatever these calls produce.
 */
@Injectable({ providedIn: 'root' })
export class VaultApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  // --- Reference data (dynamic-categories spec: per-vault schema) ---
  getCategorySchema(userId: string): Observable<Category[]> {
    return this.http.get<Category[]>(`${this.base}/api/vaults/${userId}/metadata/categories`);
  }

  // --- Vault (user) ---
  createVault(userId: string, displayName: string): Observable<VaultSummary> {
    return this.http.post<VaultSummary>(`${this.base}/api/vaults`, { userId, displayName });
  }

  getVault(userId: string): Observable<VaultSummary> {
    return this.http.get<VaultSummary>(`${this.base}/api/vaults/${userId}`);
  }

  getCategory(userId: string, categoryId: string): Observable<CategoryView> {
    return this.http.get<CategoryView>(`${this.base}/api/vaults/${userId}/categories/${categoryId}`);
  }

  /**
   * Saves every edit made to one category in a single request, including the
   * full contents of each collection the user touched. The backend validates
   * the whole set before writing any of it and records one event per changed
   * field; the response is the category's values as they now stand.
   */
  updateCategoryFields(
    userId: string,
    categoryId: string,
    changes: UpdateCategoryFieldsRequest,
  ): Observable<CategoryView> {
    return this.http.put<CategoryView>(
      `${this.base}/api/vaults/${userId}/categories/${categoryId}/fields`, changes);
  }

  getActivity(userId: string): Observable<ActivityEntry[]> {
    return this.http.get<ActivityEntry[]>(`${this.base}/api/vaults/${userId}/activity`);
  }

  // --- Category/field schema management (dynamic-categories spec) ---
  createCategory(userId: string, name: string): Observable<Category> {
    return this.http.post<Category>(`${this.base}/api/vaults/${userId}/categories`, { name });
  }

  renameCategory(userId: string, categoryId: string, name: string): Observable<void> {
    return this.http.put<void>(`${this.base}/api/vaults/${userId}/categories/${categoryId}`, { name });
  }

  deleteCategory(userId: string, categoryId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/api/vaults/${userId}/categories/${categoryId}`);
  }

  createField(
    userId: string,
    categoryId: string,
    field: {
      name: string;
      /** Optional - the backend defaults an untyped field to `Text`. */
      fieldType?: FieldType | null;
      autocompleteToken?: string | null;
      choices?: string[] | null;
      parentFieldDefinitionId?: string | null;
      isSecret?: boolean;
      itemNoun?: string | null;
    },
  ): Observable<FieldDefinition> {
    return this.http.post<FieldDefinition>(`${this.base}/api/vaults/${userId}/categories/${categoryId}/fields`, {
      name: field.name,
      fieldType: field.fieldType ?? null,
      autocompleteToken: field.autocompleteToken ?? null,
      choices: field.choices ?? null,
      parentFieldDefinitionId: field.parentFieldDefinitionId ?? null,
      isSecret: field.isSecret ?? false,
      itemNoun: field.itemNoun ?? null,
    });
  }

  renameField(userId: string, categoryId: string, fieldId: string, name: string): Observable<void> {
    return this.http.put<void>(`${this.base}/api/vaults/${userId}/categories/${categoryId}/fields/${fieldId}`, { name });
  }

  deleteField(userId: string, categoryId: string, fieldId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/api/vaults/${userId}/categories/${categoryId}/fields/${fieldId}`);
  }

  // --- Sharing (user) ---
  listGrants(userId: string): Observable<Grant[]> {
    return this.http.get<Grant[]>(`${this.base}/api/vaults/${userId}/grants`);
  }

  share(userId: string, request: ShareRequest): Observable<Grant> {
    return this.http.post<Grant>(`${this.base}/api/vaults/${userId}/shares`, request);
  }

  revoke(userId: string, grantId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/api/vaults/${userId}/shares/${grantId}/revoke`, {});
  }

  renew(userId: string, grantId: string, newDuration: ShareDuration): Observable<Grant> {
    return this.http.post<Grant>(`${this.base}/api/vaults/${userId}/shares/${grantId}/renew`, { newDuration });
  }

  /** Moves a live share's end date to an exact instant (ISO-8601). */
  changeGrantExpiry(userId: string, grantId: string, newExpiresAt: string): Observable<Grant> {
    return this.http.put<Grant>(`${this.base}/api/vaults/${userId}/shares/${grantId}/expiry`, { newExpiresAt });
  }

  // --- Share codes (user) ---
  listShareCodes(userId: string): Observable<ShareCode[]> {
    return this.http.get<ShareCode[]>(`${this.base}/api/vaults/${userId}/share-codes`);
  }

  /** The response carries the only copy of the plaintext code the user will ever see. */
  generateShareCode(userId: string, request: GenerateShareCodeRequest): Observable<GeneratedShareCode> {
    return this.http.post<GeneratedShareCode>(`${this.base}/api/vaults/${userId}/share-codes`, request);
  }

  revokeShareCode(userId: string, shareCodeId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/api/vaults/${userId}/share-codes/${shareCodeId}/revoke`, {});
  }

  listPendingShareRequests(userId: string): Observable<PendingShareRequest[]> {
    return this.http.get<PendingShareRequest[]>(`${this.base}/api/vaults/${userId}/share-requests`);
  }

  approveShareRequest(userId: string, shareCodeId: string, consentMethod: ConsentMethod): Observable<string[]> {
    return this.http.post<string[]>(
      `${this.base}/api/vaults/${userId}/share-requests/${shareCodeId}/approve`, { consentMethod });
  }

  rejectShareRequest(userId: string, shareCodeId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/api/vaults/${userId}/share-requests/${shareCodeId}/reject`, {});
  }

  // --- Organisations (registry) ---
  searchOrganisations(query?: string): Observable<Organisation[]> {
    const q = query ? `?query=${encodeURIComponent(query)}` : '';
    return this.http.get<Organisation[]>(`${this.base}/api/organisations${q}`);
  }

  getOrganisation(organisationId: string): Observable<Organisation> {
    return this.http.get<Organisation>(`${this.base}/api/organisations/${organisationId}`);
  }

  registerOrganisation(request: RegisterOrganisationRequest): Observable<Organisation> {
    return this.http.post<Organisation>(`${this.base}/api/organisations`, request);
  }

  updateOrganisationProfile(organisationId: string, request: UpdateOrganisationProfileRequest): Observable<Organisation> {
    return this.http.put<Organisation>(`${this.base}/api/organisations/${organisationId}/profile`, request);
  }

  updateOrganisationCompliance(organisationId: string, request: UpdateOrganisationComplianceRequest): Observable<Organisation> {
    return this.http.put<Organisation>(`${this.base}/api/organisations/${organisationId}/compliance`, request);
  }

  /** The fixed set of system category names every vault seeds, for the per-category agreement step. */
  getCategoryCatalog(): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/api/organisations/category-catalog`);
  }

  listCategoryAgreements(organisationId: string): Observable<CategoryAgreementView[]> {
    return this.http.get<CategoryAgreementView[]>(`${this.base}/api/organisations/${organisationId}/agreements`);
  }

  setCategoryAgreement(organisationId: string, request: SetCategoryAgreementRequest): Observable<CategoryAgreementView> {
    return this.http.put<CategoryAgreementView>(`${this.base}/api/organisations/${organisationId}/agreements`, request);
  }

  clearCategoryAgreement(organisationId: string, categoryName: string): Observable<CategoryAgreementView> {
    return this.http.delete<CategoryAgreementView>(
      `${this.base}/api/organisations/${organisationId}/agreements/${encodeURIComponent(categoryName)}`);
  }

  addPendingInvites(organisationId: string, emails: string[]): Observable<string[]> {
    return this.http.post<string[]>(`${this.base}/api/organisations/${organisationId}/invites`, { emails });
  }

  getAgreement(organisationId: string): Observable<Agreement> {
    return this.http.get<Agreement>(`${this.base}/api/organisations/${organisationId}/agreement`);
  }

  redeemShareCode(organisationId: string, code: string): Observable<ShareCodeRedemptionView> {
    return this.http.post<ShareCodeRedemptionView>(
      `${this.base}/v1/share-codes/redeem`,
      { code },
      { headers: { 'X-Org-Id': organisationId } },
    );
  }

  listOrganisationGrants(organisationId: string): Observable<Grant[]> {
    return this.http.get<Grant[]>(`${this.base}/v1/grants`, {
      headers: { 'X-Org-Id': organisationId },
    });
  }

  getOrganisationCategory(organisationId: string, userId: string, categoryId: string): Observable<CategoryView> {
    return this.http.get<CategoryView>(`${this.base}/v1/vault/${userId}/categories/${categoryId}`, {
      headers: { 'X-Org-Id': organisationId },
    });
  }

  /** Every share-code request this organisation has redeemed, whatever its current status. */
  listOrganisationShareRequests(organisationId: string): Observable<OrganisationShareRequest[]> {
    return this.http.get<OrganisationShareRequest[]>(`${this.base}/v1/share-requests`, {
      headers: { 'X-Org-Id': organisationId },
    });
  }
}
