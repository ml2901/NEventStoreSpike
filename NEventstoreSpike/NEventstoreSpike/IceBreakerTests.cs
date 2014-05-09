using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CommonDomain;
using CommonDomain.Persistence;
using CommonDomain.Persistence.EventStore;
using FluentAssertions;
using NEventStore;
using NEventStore.Dispatcher;
using NEventStore.Persistence.SqlPersistence.SqlDialects;
using Xunit;
using CommonDomain.Core;

namespace Neuraxis.Services.Execution.Tests
{
    public class IceBreakerTests
    {
        [Fact]
        public void CanCreateAndSaveAggregateToEventstore()
        {
            Commit commit = null;
            var store = Wireup.Init()
                              .LogToOutputWindow()
                              .UsingInMemoryPersistence()
                              .UsingJsonSerialization()
                              .UsingSynchronousDispatchScheduler()
                              .DispatchTo(new DelegateMessageDispatcher(c => commit = c))
                              .Build();
            var factory = new AggregateFactory();
            var conflictDetector = new ConflictDetector();

            var repository = new EventStoreRepository(store, factory, conflictDetector);

            var aggregateId = Guid.NewGuid();

            const string expectedValue = "InitialValue";
            var aggregate = new SomeAggregate(aggregateId, expectedValue);
            repository.Save(aggregate, aggregateId, null);

            var actual = repository.GetById<SomeAggregate>(aggregateId);


            actual.Value.Should().Be(expectedValue);
            commit.Events.Count.Should().Be(1);
        }


        [Fact]
        public void CanUpdateAndSaveAggregateToEventstore()
        {

            var aggregateId = Guid.NewGuid();
            const string initialValue = "InitialValue";
            const string expectedValue = "SomeValue";

            using (var store = CreateEventStore())
            {
                using (var repository = new EventStoreRepository(store, new AggregateFactory(), new ConflictDetector()))
                {
                    var aggregate = new SomeAggregate(aggregateId, initialValue);
                    repository.Save(aggregate, aggregateId, null);
                }


                using (var repository = new EventStoreRepository(store, new AggregateFactory(), new ConflictDetector()))
                {
                    var reloadedAggregate = repository.GetById<SomeAggregate>(aggregateId);
                    reloadedAggregate.ChangeValue(expectedValue);
                    repository.Save(reloadedAggregate, aggregateId, null);
                }


                using (var repository = new EventStoreRepository(store, new AggregateFactory(), new ConflictDetector()))
                {
                    var actual = repository.GetById<SomeAggregate>(aggregateId);
                    actual.Value.Should().Be(expectedValue);
                }
            }
        }

        private static IStoreEvents CreateEventStore()
        {
            var storeBuilder = Wireup.Init()
                                     .LogToOutputWindow()
                                     .UsingSqlPersistence("TestDb")
                                     .WithDialect(new SqliteDialect())
                                     .InitializeStorageEngine()
                                     .UsingJsonSerialization();
            return storeBuilder.Build();
        }
    }

    internal class AggregateFactory : IConstructAggregates
    {
        public IAggregate Build(Type type, Guid id, IMemento snapshot)
        {
            ConstructorInfo constructor = type.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Guid) }, null);


            return constructor.Invoke(new object[] { id }) as IAggregate;
        }
    }


    public class SomeAggregate : AggregateBase
    {
        private string _value;

        public string Value
        {
            get { return _value; }
        }

        private SomeAggregate(Guid id)
        {
            Id = id;
        }

        public SomeAggregate(Guid id, string value):this(id)
        {
            RaiseEvent(new SomeAggregateCreatedEvent(value));    
        }

        public void ChangeValue(string newValue)
        {
            RaiseEvent(new SomeAggregateValueChangedEvent(newValue));
        }


        private void Apply(SomeAggregateCreatedEvent @event)
        {
            _value = @event.Value;
        }

        private void Apply(SomeAggregateValueChangedEvent @event)
        {
            _value = @event.NewValue;
        }
        
    }

    public class SomeAggregateValueChangedEvent
    {
        public string NewValue;

        public SomeAggregateValueChangedEvent(string newValue)
        {
            NewValue = newValue;
        }
    }

    public class SomeAggregateCreatedEvent
    {
        public readonly string Value;

        public SomeAggregateCreatedEvent(string value)
        {
            Value = value;
        }
    }
    
}
