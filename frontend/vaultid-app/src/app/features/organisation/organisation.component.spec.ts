import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { of } from 'rxjs';
import { VaultApiService } from '../../services/vault-api.service';
import { OrganisationComponent } from './organisation.component';

describe('OrganisationComponent', () => {
  let fixture: ComponentFixture<OrganisationComponent>;
  let api: jasmine.SpyObj<VaultApiService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<VaultApiService>('VaultApiService', [
      'searchOrganisations',
      'redeemShareCode',
      'listOrganisationGrants',
      'getOrganisationCategory',
    ]);

    api.searchOrganisations.and.returnValue(of([
      {
        id: 'org-fnb',
        name: 'FNB Bank',
        status: 'approved',
        agreement: {
          organisationId: 'org-fnb',
          organisationName: 'FNB Bank',
          agreementId: 'agreement-1',
          purpose: 'To process your loan application.',
          retentionDays: 365,
          legalBasis: 'Contractual obligation',
          thirdPartySharing: 'Credit bureaus for affordability checks.',
          deletionCommitment: 'Cached snapshots are deleted within 30 days.',
        },
      },
    ]));

    api.redeemShareCode.and.returnValue(of({
      shareCodeId: 'share-1',
      userId: 'user-123',
      status: 'AwaitingApproval',
      fieldCount: 3,
      accessExpiresAt: '2026-10-10T00:00:00Z',
    }));

    api.listOrganisationGrants.and.returnValue(of([]));
    api.getOrganisationCategory.and.returnValue(of({
      categoryId: 'cat-1',
      fields: { fullName: 'Alice Smith' },
      collections: {},
    }));

    await TestBed.configureTestingModule({
      imports: [OrganisationComponent],
      providers: [{ provide: VaultApiService, useValue: api }],
    }).compileComponents();

    fixture = TestBed.createComponent(OrganisationComponent);
    fixture.detectChanges();
  });

  it('does not render mock example share codes or fake records', () => {
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Organisation access');
    expect(text).not.toContain('Example valid codes');
    expect(text).not.toContain('VLT-2048');
  });

  it('redeems a valid code through the organisation API using the logged-in organisation', () => {
    const input = fixture.nativeElement.querySelector('input[placeholder="Enter share code"]') as HTMLInputElement;
    const button = fixture.nativeElement.querySelector('[data-testid="use-code-btn"]') as HTMLButtonElement;

    input.value = 'CODE-123';
    input.dispatchEvent(new Event('input'));
    button.click();
    fixture.detectChanges();

    expect(api.redeemShareCode).toHaveBeenCalledWith('org-fnb', 'CODE-123');
    expect(fixture.nativeElement.textContent).toContain('Awaiting approval');
  });

  it('polls for approval and shows the shared full name with live data once the grant becomes active', fakeAsync(() => {
    api.redeemShareCode.and.returnValue(of({
      shareCodeId: 'share-1',
      userId: 'user-123',
      status: 'AwaitingApproval',
      fieldCount: 1,
      accessExpiresAt: '2026-10-10T00:00:00Z',
    }));

    api.listOrganisationGrants.and.returnValues(
      of([]),
      of([{ grantId: 'grant-1', userId: 'user-123', organisationId: 'org-fnb', organisationName: 'FNB Bank', categoryId: 'cat-1', scope: 'ReadOnly', duration: 'Custom', agreementId: 'agreement-1', expiresAt: '2026-10-10T00:00:00Z', status: 'Active', consentedAt: '2026-09-10T00:00:00Z', fieldDefinitionIds: ['field-1'] }]),
      of([{ grantId: 'grant-1', userId: 'user-123', organisationId: 'org-fnb', organisationName: 'FNB Bank', categoryId: 'cat-1', scope: 'ReadOnly', duration: 'Custom', agreementId: 'agreement-1', expiresAt: '2026-10-10T00:00:00Z', status: 'Active', consentedAt: '2026-09-10T00:00:00Z', fieldDefinitionIds: ['field-1'] }]),
    );

    const input = fixture.nativeElement.querySelector('input[placeholder="Enter share code"]') as HTMLInputElement;
    const button = fixture.nativeElement.querySelector('[data-testid="use-code-btn"]') as HTMLButtonElement;

    input.value = 'CODE-123';
    input.dispatchEvent(new Event('input'));
    button.click();
    fixture.detectChanges();

    tick(5000);
    fixture.detectChanges();

    expect(api.getOrganisationCategory).toHaveBeenCalledWith('org-fnb', 'user-123', 'cat-1');
    expect(fixture.nativeElement.textContent).toContain('Alice Smith');
    expect(fixture.nativeElement.textContent).not.toContain('Jane Doe');
    expect(fixture.nativeElement.textContent).toContain('Live data is now visible below');
  }));
});
