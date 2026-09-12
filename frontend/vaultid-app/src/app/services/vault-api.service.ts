import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  ActivityEntry, Agreement, Category, CategoryView, ConsentMethod, FieldDefinition, FieldType,
  GeneratedShareCode, GenerateShareCodeRequest, Grant, Organisation, PendingShareRequest,
  ShareCode, ShareDuration, ShareRequest, VaultSummary,
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

  updateField(userId: string, fieldDefinitionId: string, value: string): Observable<void> {
    return this.http.put<void>(`${this.base}/api/vaults/${userId}/fields/${fieldDefinitionId}`, { value });
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
    },
  ): Observable<FieldDefinition> {
    return this.http.post<FieldDefinition>(`${this.base}/api/vaults/${userId}/categories/${categoryId}/fields`, {
      name: field.name,
      fieldType: field.fieldType ?? null,
      autocompleteToken: field.autocompleteToken ?? null,
      choices: field.choices ?? null,
      parentFieldDefinitionId: field.parentFieldDefinitionId ?? null,
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

  getAgreement(organisationId: string): Observable<Agreement> {
    return this.http.get<Agreement>(`${this.base}/api/organisations/${organisationId}/agreement`);
  }
}
