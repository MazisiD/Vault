import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { SharingComponent } from './sharing.component';
import { VaultApiService } from '../../services/vault-api.service';
import { SessionService } from '../../services/session.service';
import { Category, GeneratedShareCode, Organisation, PendingShareRequest, ShareCode } from '../../models';

const USER_ID = 'user-1';

const ORG: Organisation = { id: 'org-1', name: 'Acme Bank', status: 'approved' };

const CATEGORY: Category = {
  id: 'cat-1',
  name: 'Biographical',
  isSystem: true,
  fields: [
    { id: 'f-1', categoryId: 'cat-1', name: 'FullName', fieldType: 'Text', sortOrder: 0, children: [] },
    { id: 'f-2', categoryId: 'cat-1', name: 'TaxNumber', fieldType: 'Text', sortOrder: 1, children: [] },
    {
      id: 'g-1',
      categoryId: 'cat-1',
      name: 'NextOfKin',
      fieldType: 'Group',
      sortOrder: 2,
      children: [
        { id: 'f-3', categoryId: 'cat-1', parentFieldDefinitionId: 'g-1', name: 'PhoneNumber', fieldType: 'Text', sortOrder: 0, children: [] },
      ],
    },
  ],
};

const GENERATED: GeneratedShareCode = {
  shareCodeId: 'sc-1',
  code: 'ABCD-2345',
  organisationId: ORG.id,
  organisationName: ORG.name,
  codeExpiresAt: '2030-01-08T00:00:00Z',
  accessExpiresAt: '2030-02-01T00:00:00Z',
};

const PENDING: PendingShareRequest = {
  shareCodeId: 'sc-1',
  organisationId: ORG.id,
  organisationName: ORG.name,
  fields: [{ fieldDefinitionId: 'f-1', categoryId: 'cat-1', categoryName: 'Biographical', fieldName: 'FullName' }],
  accessExpiresAt: '2030-02-01T00:00:00Z',
  requestedAt: '2030-01-02T00:00:00Z',
  agreement: {
    organisationId: ORG.id,
    organisationName: ORG.name,
    agreementId: 'ag-1',
    purpose: 'Account opening',
    retentionDays: 365,
    legalBasis: 'Contractual obligation',
    thirdPartySharing: null,
    deletionCommitment: null,
  },
};

describe('SharingComponent', () => {
  let api: jasmine.SpyObj<VaultApiService>;

  function setup(overrides: { pending?: PendingShareRequest[]; codes?: ShareCode[] } = {}) {
    api = jasmine.createSpyObj<VaultApiService>('VaultApiService', [
      'getCategorySchema', 'listGrants', 'listShareCodes', 'listPendingShareRequests',
      'searchOrganisations', 'generateShareCode', 'approveShareRequest', 'rejectShareRequest',
      'revokeShareCode', 'revoke', 'changeGrantExpiry',
    ]);

    api.getCategorySchema.and.returnValue(of([CATEGORY]));
    api.listGrants.and.returnValue(of([]));
    api.listShareCodes.and.returnValue(of(overrides.codes ?? []));
    api.listPendingShareRequests.and.returnValue(of(overrides.pending ?? []));
    api.searchOrganisations.and.returnValue(of([ORG]));
    api.generateShareCode.and.returnValue(of(GENERATED));
    api.approveShareRequest.and.returnValue(of(['grant-1']));
    api.rejectShareRequest.and.returnValue(of(void 0));
    api.revokeShareCode.and.returnValue(of(void 0));

    TestBed.configureTestingModule({
      imports: [SharingComponent],
      providers: [
        { provide: VaultApiService, useValue: api },
        { provide: SessionService, useValue: { userId: () => USER_ID } },
      ],
    });

    const fixture = TestBed.createComponent(SharingComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('shows a Share button in the header that opens the generate modal', () => {
    const fixture = setup();
    const host: HTMLElement = fixture.nativeElement;

    const shareButton = host.querySelector<HTMLButtonElement>('.vid-screen-head .btn');
    expect(shareButton?.textContent).toContain('Share');
    expect(shareButton?.querySelector('i.ph-share-network')).toBeTruthy();

    shareButton!.click();
    fixture.detectChanges();

    expect(host.querySelector('.dialog-title')?.textContent).toContain('Share information');
    expect(host.querySelector('.dialog-actions .btn-primary')?.textContent).toContain('Generate share code');
  });

  it('groups the pickable fields by category and lifts Group children to the top level', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    const picker = component.picker();
    expect(picker.length).toBe(1);
    expect(picker[0].name).toBe('Biographical');
    expect(picker[0].fields.map((f) => f.id)).toEqual(['f-1', 'f-2', 'f-3']);
    // A Group is a container, so its child is labelled under the parent.
    expect(picker[0].fields[2].label).toContain('Next of kin');
  });

  it('selects and clears every field in a category from the select-all toggle', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const category = component.picker()[0];

    component.toggleCategory(category);
    expect(component.selectedCount()).toBe(3);
    expect(component.allSelected(category)).toBeTrue();

    component.toggleCategory(category);
    expect(component.selectedCount()).toBe(0);
  });

  it('only enables Generate once an organisation, a field and an end date are chosen', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.openShare();
    expect(component.canGenerate()).withContext('no organisation yet').toBeFalse();

    component.orgId = ORG.id;
    expect(component.canGenerate()).withContext('no fields yet').toBeFalse();

    component.toggleField('f-1');
    expect(component.canGenerate()).toBeTrue();
  });

  it('posts only the ticked fields and then shows the code', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.openShare();
    component.orgId = ORG.id;
    component.toggleField('f-1');
    component.toggleField('f-3');
    component.generate();
    fixture.detectChanges();

    const [userId, request] = api.generateShareCode.calls.mostRecent().args;
    expect(userId).toBe(USER_ID);
    expect(request.organisationId).toBe(ORG.id);
    expect(request.fieldDefinitionIds.sort()).toEqual(['f-1', 'f-3']);
    expect(request.scope).toBe('ReadOnly');
    // The picker collects local wall-clock time; the API always gets an instant.
    expect(request.accessExpiresAt).toMatch(/Z$/);

    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('[data-testid="generated-code"]')?.textContent?.trim()).toBe('ABCD-2345');
  });

  it('lists a redeemed request and approves it on demand', () => {
    const fixture = setup({ pending: [PENDING] });
    const host: HTMLElement = fixture.nativeElement;

    expect(host.querySelector('.vid-request .card-title')?.textContent).toContain('Acme Bank');
    expect(host.querySelector('.vid-request-fields .tag')?.textContent).toContain('Full name');

    const approve = host.querySelector<HTMLButtonElement>('.vid-request-actions .btn-primary');
    expect(approve?.textContent).toContain('Review & Approve');

    approve!.click();
    expect(api.approveShareRequest).toHaveBeenCalledWith(USER_ID, 'sc-1', 'InAppConfirmation');
  });

  it('rejects a redeemed request without granting anything', () => {
    const fixture = setup({ pending: [PENDING] });
    const host: HTMLElement = fixture.nativeElement;

    host.querySelector<HTMLButtonElement>('.vid-request-actions .btn-danger')!.click();

    expect(api.rejectShareRequest).toHaveBeenCalledWith(USER_ID, 'sc-1');
    expect(api.approveShareRequest).not.toHaveBeenCalled();
  });
});
