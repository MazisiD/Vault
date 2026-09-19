using VaultID.Application.EventSourcing;
using VaultID.Domain;
using VaultID.Domain.Categories;
using VaultID.Domain.Events;
using VaultID.Services.Abstractions;
using VaultID.Services.InMemory;
using Xunit;

namespace VaultID.Tests.Application;

public sealed class VaultProjectorTests
{
    [Fact]
    public void VaultCreated_SeedsCategoriesAndFieldDefinitions()
    {
        var categoryId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();

        var events = new DomainEvent[]
        {
            new VaultCreated
            {
                VaultId = "user-1",
                DisplayName = "Jane",
                Categories = [new Category { Id = categoryId, Name = "Biographical", IsSystem = true }],
                Fields = [new FieldDefinition { Id = fieldId, CategoryId = categoryId, Name = "FullName", FieldType = FieldType.Text, SortOrder = 0 }]
            }
        };

        var state = VaultProjector.Project("user-1", events);

        Assert.True(state.Exists);
        Assert.Equal("Jane", state.DisplayName);
        Assert.True(state.Categories.ContainsKey(categoryId));
        Assert.True(state.FieldDefinitions.ContainsKey(fieldId));
        Assert.Empty(state.GetCategoryValues(categoryId));
    }

    [Fact]
    public void FieldUpdated_StoresValueUnderTheFieldsOwningCategory()
    {
        var categoryId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();

        var events = new DomainEvent[]
        {
            new VaultCreated
            {
                VaultId = "user-1",
                DisplayName = "Jane",
                Categories = [new Category { Id = categoryId, Name = "Biographical", IsSystem = true }],
                Fields = [new FieldDefinition { Id = fieldId, CategoryId = categoryId, Name = "FullName", FieldType = FieldType.Text, SortOrder = 0 }]
            },
            new FieldUpdated { VaultId = "user-1", FieldDefinitionId = fieldId, NewValue = "Jane Doe" }
        };

        var state = VaultProjector.Project("user-1", events);

        Assert.Equal("Jane Doe", state.GetCategoryValues(categoryId)[fieldId]);
    }

    [Fact]
    public void CategoryCreated_ThenRenamed_UpdatesName()
    {
        var categoryId = Guid.NewGuid();
        var events = new DomainEvent[]
        {
            new VaultCreated { VaultId = "user-1", DisplayName = "Jane", Categories = [], Fields = [] },
            new CategoryCreated { VaultId = "user-1", CategoryId = categoryId, Name = "Hobbies", IsSystem = false },
            new CategoryRenamed { VaultId = "user-1", CategoryId = categoryId, NewName = "Interests" }
        };

        var state = VaultProjector.Project("user-1", events);

        Assert.Equal("Interests", state.Categories[categoryId].Name);
    }

    [Fact]
    public void CategoryDeleted_RemovesCategoryAndItsValueBucket()
    {
        var categoryId = Guid.NewGuid();
        var events = new DomainEvent[]
        {
            new VaultCreated { VaultId = "user-1", DisplayName = "Jane", Categories = [], Fields = [] },
            new CategoryCreated { VaultId = "user-1", CategoryId = categoryId, Name = "Hobbies", IsSystem = false },
            new CategoryDeleted { VaultId = "user-1", CategoryId = categoryId }
        };

        var state = VaultProjector.Project("user-1", events);

        Assert.False(state.Categories.ContainsKey(categoryId));
        Assert.False(state.Values.ContainsKey(categoryId));
    }

    [Fact]
    public void FieldDefinitionCreated_ThenUpdated_ThenDeleted_RemovesFieldAndAnyValue()
    {
        var categoryId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();

        var events = new DomainEvent[]
        {
            new VaultCreated
            {
                VaultId = "user-1",
                DisplayName = "Jane",
                Categories = [new Category { Id = categoryId, Name = "Hobbies", IsSystem = false }],
                Fields = []
            },
            new FieldDefinitionCreated
            {
                VaultId = "user-1", FieldDefinitionId = fieldId, CategoryId = categoryId,
                Name = "Favourite Sport", FieldType = FieldType.Text, SortOrder = 0
            },
            new FieldUpdated { VaultId = "user-1", FieldDefinitionId = fieldId, NewValue = "Cricket" },
            new FieldDefinitionUpdated { VaultId = "user-1", FieldDefinitionId = fieldId, NewName = "Sport" },
            new FieldDefinitionDeleted { VaultId = "user-1", FieldDefinitionId = fieldId }
        };

        var state = VaultProjector.Project("user-1", events);

        Assert.False(state.FieldDefinitions.ContainsKey(fieldId));
    }

    [Fact]
    public async Task AppendAsync_RetriesAfterAConcurrentStreamAdvance()
    {
        var eventStore = new InMemoryEventStore();
        var repository = new VaultStreamRepository(eventStore, new EventSerializer());

        await repository.AppendAsync("user-1", 0, new[]
        {
            new VaultCreated
            {
                VaultId = "user-1",
                DisplayName = "Jane",
                Categories = [],
                Fields = []
            }
        });

        var staleState = await repository.LoadStateAsync("user-1");
        var serializer = new EventSerializer();
        await eventStore.AppendAsync("user-1", staleState.Version, [serializer.Serialize(new CategoryCreated
        {
            VaultId = "user-1",
            CategoryId = Guid.NewGuid(),
            Name = "Hobbies",
            IsSystem = false
        })]);

        var newCategoryId = Guid.NewGuid();
        var ex = await Record.ExceptionAsync(() => repository.AppendAsync("user-1", staleState.Version, [new CategoryCreated
        {
            VaultId = "user-1",
            CategoryId = newCategoryId,
            Name = "Interests",
            IsSystem = false
        }]));

        Assert.Null(ex);

        var state = await repository.LoadStateAsync("user-1");
        Assert.Equal(3, state.Version);
        Assert.Contains(newCategoryId, state.Categories.Keys);
    }

    [Fact]
    public void FieldDefinitionUpdated_ChangesFieldTypeAndChoicesMetadata()
    {
        var categoryId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();

        var events = new DomainEvent[]
        {
            new VaultCreated
            {
                VaultId = "user-1",
                DisplayName = "Jane",
                Categories = [new Category { Id = categoryId, Name = "Biographical", IsSystem = true }],
                Fields = [new FieldDefinition { Id = fieldId, CategoryId = categoryId, Name = "Gender", FieldType = FieldType.Text, SortOrder = 0 }]
            },
            new FieldDefinitionUpdated
            {
                VaultId = "user-1",
                FieldDefinitionId = fieldId,
                NewFieldType = FieldType.Choice,
                NewChoices = ["Female", "Male", "Other"]
            }
        };

        var state = VaultProjector.Project("user-1", events);

        Assert.Equal(FieldType.Choice, state.FieldDefinitions[fieldId].FieldType);
        Assert.Equal(["Female", "Male", "Other"], state.FieldDefinitions[fieldId].Choices);
    }

    [Fact]
    public void CategoryShared_ProjectsAGrantKeyedByCategoryId()
    {
        var categoryId = Guid.NewGuid();
        var grantId = Guid.NewGuid();

        var events = new DomainEvent[]
        {
            new VaultCreated { VaultId = "user-1", DisplayName = "Jane", Categories = [], Fields = [] },
            new CategoryShared
            {
                VaultId = "user-1", GrantId = grantId, OrganisationId = "org-1", CategoryId = categoryId,
                Scope = AccessScope.ReadOnly, Duration = ShareDuration.ThirtyDays, AgreementId = "agreement-1"
            }
        };

        var state = VaultProjector.Project("user-1", events);

        Assert.True(state.Grants.ContainsKey(grantId));
        Assert.Equal(categoryId, state.Grants[grantId].CategoryId);
    }
}
