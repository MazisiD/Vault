import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RegisterComponent } from './register.component';
import { AuthService } from '../../../services/auth.service';

describe('RegisterComponent', () => {
  let fixture: ComponentFixture<RegisterComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            register: jasmine.createSpy('register').and.resolveTo({ ok: true }),
            user: () => null,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
  });

  it('shows both account type choices', () => {
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Register as an individual');
    expect(text).toContain('Register as an organisation or company');
  });

  it('switches to the organisation form when selected', () => {
    const buttons = fixture.nativeElement.querySelectorAll('button[type="button"]');
    buttons[1].click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Organisation or company name');
  });
});
