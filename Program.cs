using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics; // IDE hover

namespace Households
{
    public static class Constants
    {
        public static int nThreads = 1;
        public static int maxSizeHusholdning = 10;
    }

    public enum LifecycleType
    {
        Birth,
        Death
    }

    class Program
    {
        static void Main(string[] args)
        {
            var sim = new Simulation();
            sim.InitializeBucket(size: 1, cap: 10);
            sim.InitializeBucket(size: 2, cap: 10);
            sim.InitializeBucket(size: 3, cap: 10);

            sim.CreateHousehold(101, new[] { 45, 21 }, new[] { "Prof bachelor", "Almen gym" });
            sim.CreateHousehold(102, new[] { 64, 62 }, new[] { "Kandidat", "Prof bachelor" });
            sim.CreateHousehold(103, new[] { 32, 5 }, new[] { "PhD", "Ingen" });

            sim.PrintState();

            sim.RunYearlySimulationCycle();

            sim.PrintState();
        }
    }

    /// <summary>
    /// Husholdnings-"objekt"
    /// </summary>
    [DebuggerDisplay("Household ID: {Id}, Size: {Size}")]
    [DebuggerTypeProxy(typeof(HouseholdDebugView))]
    public readonly struct Household
    {
        private readonly HouseholdsOfGivenSize _bucket;
        private readonly int _localHIdx;

        public int Id { get; }
        public int Size => _bucket.HouseholdSize;

        public Household(HouseholdsOfGivenSize bucket, int localHouseholdIndex, int id)
        {
            _bucket = bucket;
            _localHIdx = localHouseholdIndex;
            Id = id;
        }

        public PersonList Members => new PersonList(_bucket, _localHIdx, Size);

        /// <summary>
        /// Til brug for debugging
        /// </summary>
        internal class HouseholdDebugView
        {
            private readonly Household _household;
            public HouseholdDebugView(Household household) => _household = household;

            public int Id => _household.Id;
            public int Size => _household.Size;

            /// <summary>
            /// Husholdningens personer vises med hover-over som "Persons", som ligner en List&lt;Person>
            /// </summary>
            public Person[] Persons
            {
                get
                {
                    var items = new Person[_household.Size];
                    for (int i = 0; i < _household.Size; i++)
                    {
                        items[i] = _household.Members[i];
                    }
                    return items;
                }
            }
        }
    }

    /// <summary>
    /// Person-"objekt"
    /// </summary>
    [DebuggerDisplay("Age = {Age}, Hf = {Hf}")]
    public readonly struct Person
    {
        private readonly HouseholdsOfGivenSize _storage;
        public int AbsIdx { get; }

        public Person(HouseholdsOfGivenSize bucket, int absoluteIndex)
        {
            _storage = bucket;
            AbsIdx = absoluteIndex;
        }

        public int Age
        {
            get => _storage.Ages[AbsIdx];
            set => _storage.Ages[AbsIdx] = value;
        }

        public string Hf
        {
            get => _storage.Hfs[AbsIdx];
            set => _storage.Hfs[AbsIdx] = value;
        }

        /// <summary>
        /// Peger tilbage til personens husholdning
        /// </summary>
        public Household Household
        {
            get
            {
                int localHIdx = AbsIdx / _storage.HouseholdSize;
                int hhId = _storage.GetHouseholdId(localHIdx);
                return new Household(_storage, localHIdx, hhId);
            }
        }
    }

    /// <summary>
    /// For at kunne indeksere personer i husholdningen efter nummer og loope over dem.
    /// </summary>
    [DebuggerDisplay("Count = {Count}")]
    public readonly struct PersonList
    {
        private readonly HouseholdsOfGivenSize _bucket;
        private readonly int _localHIdx;
        private readonly int _size;

        public PersonList(HouseholdsOfGivenSize bucket, int localHIdx, int size)
        {
            _bucket = bucket;
            _localHIdx = localHIdx;
            _size = size;
        }

        public int Count => _size;

        public Person this[int index]
        {
            get
            {
                if (index < 0 || index >= _size) throw new IndexOutOfRangeException();
                return new Person(_bucket, _localHIdx * _size + index);
            }
            set
            {
                int absIdx = _localHIdx * _size + index;
                _bucket.Ages[absIdx] = value.Age;
                _bucket.Hfs[absIdx] = value.Hf;
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(_bucket, _localHIdx, _size);

        public struct Enumerator
        {
            private readonly HouseholdsOfGivenSize _bucket;
            private readonly int _localHIdx;
            private readonly int _size;
            private int _index;

            public Enumerator(HouseholdsOfGivenSize bucket, int localHIdx, int size)
            {
                _bucket = bucket; _localHIdx = localHIdx; _size = size; _index = -1;
            }
            public bool MoveNext() => ++_index < _size;
            public Person Current => new Person(_bucket, _localHIdx * _size + _index);
        }
    }

    public struct HouseholdPosition
    {
        public int HouseholdSize;
        public int LocalIndex;
        public HouseholdPosition(int size, int idx) { HouseholdSize = size; LocalIndex = idx; }
    }

    public struct HouseholdSizeEvent
    {
        public int HouseholdId;
        public LifecycleType Type;
        public int TargetPersonAbsIdx;

        // Direkte tracking mht. buckets af hvor en husholdning befinder sig (så vi ikke skal slå det op i dict)
        public int SourceBucketSize;
        public int SourceLocalIndex;

        public HouseholdSizeEvent(int id, LifecycleType type, int sourceSize, int sourceIdx, int targetIdx = -1)
        {
            HouseholdId = id;
            Type = type;
            SourceBucketSize = sourceSize;
            SourceLocalIndex = sourceIdx;
            TargetPersonAbsIdx = targetIdx;
        }
    }

    /// <summary>
    /// Simulation af modellen
    /// </summary>
    public class Simulation
    {
        private HouseholdsOfGivenSize[] _buckets = new HouseholdsOfGivenSize[11];
        private Dictionary<int, HouseholdPosition> _registry = new Dictionary<int, HouseholdPosition>();
        private ConcurrentQueue<HouseholdSizeEvent> _structuralEventQueue = new ConcurrentQueue<HouseholdSizeEvent>();

        public void InitializeBucket(int size, int cap) => _buckets[size] = new HouseholdsOfGivenSize(size, cap);

        public void CreateHousehold(int id, int[] ages, string[] educations)
        {
            int size = ages.Length;
            if (_buckets[size] == null) InitializeBucket(size, 1000);
            int localIdx = _buckets[size].InsertHousehold(id, ages, educations);
            _registry[id] = new HouseholdPosition(size, localIdx);
        }

        public void RunYearlySimulationCycle()
        {
            for (int i = 1; i < _buckets.Length; i++)
            {
                if (_buckets[i] != null)
                {
                    _buckets[i].ParallelYearlyUpdate(_structuralEventQueue);
                }
            }

            Console.WriteLine($"\nKører {_structuralEventQueue.Count} events i 1 tråd...");
            while (_structuralEventQueue.TryDequeue(out HouseholdSizeEvent ev))
            {
                //Kun en mindre del af husholdningerne
                
                // Hurtigere end at slå op i dict
                int currentSize = ev.SourceBucketSize;
                int currentLocalIdx = ev.SourceLocalIndex;

                var bucket = _buckets[currentSize];

                // Hvis husholdningen har flere events samme år (f.eks. tvillinger), er vi nødt til at slå op i dict
                if (bucket == null || currentLocalIdx >= bucket.HouseholdCount || bucket.GetHouseholdId(currentLocalIdx) != ev.HouseholdId)
                {
                    if (!_registry.TryGetValue(ev.HouseholdId, out var registeredLoc)) continue;
                    currentSize = registeredLoc.HouseholdSize;
                    currentLocalIdx = registeredLoc.LocalIndex;
                    bucket = _buckets[currentSize];
                }

                if (ev.Type == LifecycleType.Birth)
                {
                    int newSize = currentSize + 1;
                    if (newSize > Constants.maxSizeHusholdning) throw new InvalidOperationException("Household size exceeded maximum limit of " + Constants.maxSizeHusholdning + ".");
                    if (_buckets[newSize] == null) InitializeBucket(newSize, 1000);

                    int sourceStart = currentLocalIdx * currentSize;
                    var targetBucket = _buckets[newSize];

                    // Fjern gammel householdning fra nuværende bucket vha. swap-back
                    bucket.RemoveHouseholdAndSwapLast(currentLocalIdx, (shiftedId, newIdx) =>
                    {
                        _registry[shiftedId] = new HouseholdPosition(currentSize, newIdx);
                    });

                    // Flytter eksisterende personer og tilføjer 1 ny
                    int newLocalIdx = targetBucket.MigrateFromSmallerBucket(ev.HouseholdId, bucket, sourceStart, currentSize, 0, "None");

                    // Opdaterer
                    _registry[ev.HouseholdId] = new HouseholdPosition(newSize, newLocalIdx);
                }
                else if (ev.Type == LifecycleType.Death)
                {
                    int newSize = currentSize - 1;
                    int startIdx = currentLocalIdx * currentSize;

                    int targetLocalPersonIdx = ev.TargetPersonAbsIdx - startIdx;
                    if (targetLocalPersonIdx < 0 || targetLocalPersonIdx >= currentSize)
                    {
                        targetLocalPersonIdx = currentSize - 1;
                    }

                    if (newSize == 0)
                    {
                        bucket.RemoveHouseholdAndSwapLast(currentLocalIdx, (shiftedId, newIdx) =>
                        {
                            _registry[shiftedId] = new HouseholdPosition(currentSize, newIdx);
                        });
                        _registry.Remove(ev.HouseholdId);
                    }
                    else
                    {
                        if (_buckets[newSize] == null) InitializeBucket(newSize, 1000);

                        var targetBucket = _buckets[newSize];

                        // Fjern gammel household fra nuværende bucket vha. swap-back
                        bucket.RemoveHouseholdAndSwapLast(currentLocalIdx, (shiftedId, newIdx) =>
                        {
                            _registry[shiftedId] = new HouseholdPosition(currentSize, newIdx);
                        });

                        // Flytter eksisterende personer undtagen den fjernede person
                        int newLocalIdx = targetBucket.MigrateFromLargerBucket(ev.HouseholdId, bucket, startIdx, currentSize, targetLocalPersonIdx);
                        _registry[ev.HouseholdId] = new HouseholdPosition(newSize, newLocalIdx);
                    }
                }
            }
        }

        public void PrintState()
        {
            for (int i = 1; i < _buckets.Length; i++)
            {
                if (_buckets[i] != null) _buckets[i].PrintBucketState();
            }
        }
    }

    /// <summary>
    /// Her opbevares data for familier med given HouseholdSize. Hvis HouseholdSize er 3, vil elementer 0, 1, 2 høre til første familie,
    /// elementer 3, 4, 5 til anden familie osv.
    /// </summary>
    public class HouseholdsOfGivenSize
    {
        public int HouseholdSize { get; }
        public int HouseholdCount { get; private set; }

        public int[] Ages { get; }
        public string[] Hfs { get; }
        private int[] _householdIds;

        public HouseholdsOfGivenSize(int householdSize, int initialCapacity)
        {
            HouseholdSize = householdSize;
            Ages = new int[initialCapacity * householdSize];
            Hfs = new string[initialCapacity * householdSize];
            _householdIds = new int[initialCapacity];
        }

        public int GetHouseholdId(int localHIdx) => _householdIds[localHIdx];

        public int InsertHousehold(int householdId, int[] ages, string[] educations)
        {
            int localHIdx = HouseholdCount;
            _householdIds[localHIdx] = householdId;
            int startPersonIdx = localHIdx * HouseholdSize;

            for (int i = 0; i < HouseholdSize; i++)
            {
                Ages[startPersonIdx + i] = ages[i];
                Hfs[startPersonIdx + i] = educations[i];
            }
            HouseholdCount++;
            return localHIdx;
        }

        public int MigrateFromSmallerBucket(int householdId, HouseholdsOfGivenSize oldBucket, int oldSourceStart, int oldSize, int newAge, string newHf)
        {
            int localHIdx = HouseholdCount;
            _householdIds[localHIdx] = householdId;
            int startPersonIdx = localHIdx * HouseholdSize;

            // Direkte kopi af eksisterende
            Array.Copy(oldBucket.Ages, oldSourceStart, Ages, startPersonIdx, oldSize);
            Array.Copy(oldBucket.Hfs, oldSourceStart, Hfs, startPersonIdx, oldSize);

            // Ny person tilføjes
            Ages[startPersonIdx + oldSize] = newAge;
            Hfs[startPersonIdx + oldSize] = newHf;

            HouseholdCount++;
            return localHIdx;
        }

        public int MigrateFromLargerBucket(int householdId, HouseholdsOfGivenSize oldBucket, int oldSourceStart, int oldSize, int excludeLocalIdx)
        {
            int localHIdx = HouseholdCount;
            _householdIds[localHIdx] = householdId;
            int startPersonIdx = localHIdx * HouseholdSize;

            // Kopier alt før den fjernede person
            if (excludeLocalIdx > 0)
            {
                Array.Copy(oldBucket.Ages, oldSourceStart, Ages, startPersonIdx, excludeLocalIdx);
                Array.Copy(oldBucket.Hfs, oldSourceStart, Hfs, startPersonIdx, excludeLocalIdx);
            }

            // Kopier alt efter den fjernede person
            int rightChunkSize = oldSize - 1 - excludeLocalIdx;
            if (rightChunkSize > 0)
            {
                Array.Copy(oldBucket.Ages, oldSourceStart + excludeLocalIdx + 1, Ages, startPersonIdx + excludeLocalIdx, rightChunkSize);
                Array.Copy(oldBucket.Hfs, oldSourceStart + excludeLocalIdx + 1, Hfs, startPersonIdx + excludeLocalIdx, rightChunkSize);
            }

            HouseholdCount++;
            return localHIdx;
        }

        public void RemoveHouseholdAndSwapLast(int targetLocalHIdx, Action<int, int> onLastHouseholdMoved)
        {
            int lastLocalHIdx = HouseholdCount - 1;
            if (targetLocalHIdx != lastLocalHIdx)
            {
                int targetStart = targetLocalHIdx * HouseholdSize;
                int lastStart = lastLocalHIdx * HouseholdSize;
                _householdIds[targetLocalHIdx] = _householdIds[lastLocalHIdx];

                for (int i = 0; i < HouseholdSize; i++)
                {
                    Ages[targetStart + i] = Ages[lastStart + i];
                    Hfs[targetStart + i] = Hfs[lastStart + i];
                }
                onLastHouseholdMoved(_householdIds[targetLocalHIdx], targetLocalHIdx);
            }
            HouseholdCount--;
        }

        public void ParallelYearlyUpdate(ConcurrentQueue<HouseholdSizeEvent> eventQueue)
        {
            Parallel.For(0, HouseholdCount, new ParallelOptions { MaxDegreeOfParallelism = Constants.nThreads }, h =>
            {
                int id = _householdIds[h];
                Household household = new Household(this, h, id);

                // Personer bliver ældre
                for (int i = 0; i < household.Members.Count; i++)
                {
                    Person person = household.Members[i];
                    person.Age += 1;
                }

                if (id == 101)
                {
                    eventQueue.Enqueue(new HouseholdSizeEvent(id, LifecycleType.Birth, HouseholdSize, h));
                }
                else if (id == 102)
                {
                    int deadAbsIdx = -1;
                    for (int p = 0; p < household.Members.Count; p++)
                        if (household.Members[p].Age == 65) deadAbsIdx = household.Members[p].AbsIdx;
                    eventQueue.Enqueue(new HouseholdSizeEvent(id, LifecycleType.Death, HouseholdSize, h, deadAbsIdx));
                    eventQueue.Enqueue(new HouseholdSizeEvent(id, LifecycleType.Birth, HouseholdSize, h));
                }
                else if (id == 103)
                {
                    int deadAbsIdx = -1;
                    for (int p = 0; p < household.Members.Count; p++)
                        if (household.Members[p].Age == 33) deadAbsIdx = household.Members[p].AbsIdx;
                    eventQueue.Enqueue(new HouseholdSizeEvent(id, LifecycleType.Death, HouseholdSize, h, deadAbsIdx));
                }
            });
        }

        public void PrintBucketState()
        {
            if (HouseholdCount == 0) return;
            Console.WriteLine($"\n--- Household size {HouseholdSize} (Count: {HouseholdCount}) ---");
            for (int i = 0; i < HouseholdCount; i++)
            {
                Console.Write($"  Household ID {_householdIds[i]}: ");
                int start = i * HouseholdSize;
                for (int j = 0; j < HouseholdSize; j++)
                    Console.Write($"[Age: {Ages[start + j]}, Edu: {Hfs[start + j]}] ");
                Console.WriteLine();
            }
        }
    }
}