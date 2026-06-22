using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Households
{    

    class Program2
    {
        public static void Main2(string[] args)
        {
            for (int r = 0; r < Globals.reps; r++)
            {
                DateTime t0 = DateTime.Now;
                var sim = new Simulation2();

                for (int j = 0; j < Globals.nPop2; j++)
                {
                    sim.CreateHousehold(10 * j + 0, new Person2(18, EHf.AlmenGym), new Person2(18, EHf.AlmenGym));
                    sim.CreateHousehold(10 * j + 1, new Person2(64, EHf.Kandidat), new Person2(62, EHf.ProfBachelor));
                    sim.CreateHousehold(10 * j + 2, new Person2(32, EHf.PhD), new Person2(5, EHf.Ingen));
                    sim.CreateHousehold(10 * j + 3, new Person2(25, EHf.AlmenGym), new Person2(25, EHf.AlmenGym));
                    sim.CreateHousehold(10 * j + 4, new Person2(25, EHf.AlmenGym), new Person2(25, EHf.AlmenGym));
                    sim.CreateHousehold(10 * j + 5, new Person2(25, EHf.AlmenGym), new Person2(25, EHf.AlmenGym));
                    sim.CreateHousehold(10 * j + 6, new Person2(25, EHf.AlmenGym), new Person2(25, EHf.AlmenGym));
                    sim.CreateHousehold(10 * j + 7, new Person2(25, EHf.AlmenGym), new Person2(25, EHf.AlmenGym));
                    sim.CreateHousehold(10 * j + 8, new Person2(25, EHf.AlmenGym), new Person2(25, EHf.AlmenGym));
                    sim.CreateHousehold(10 * j + 9, new Person2(25, EHf.AlmenGym), new Person2(25, EHf.AlmenGym));
                }

                DateTime t1 = DateTime.Now;
                for (int i = 0; i < Globals.simulations; i++)
                {
                    sim.RunYearlySimulationCycle();
                }
                DateTime t2 = DateTime.Now;

                Console.WriteLine("Load per size: " + Program.Pretty((double)Globals.nPop2 / (t1 - t0).TotalMilliseconds * 1000d));
                Console.WriteLine("Sim per size: " + Program.Pretty((double)Globals.nPop2 / (t2 - t1).TotalMilliseconds * 1000d));

                Console.WriteLine();
            }
        }
    }

    public class Person2
    {
        public int Age { get; set; }
        public EHf Hf { get; set; }

        public Person2(int age, EHf hf)
        {
            Age = age;
            Hf = hf;
        }
    }

    public class Household2
    {
        public int Id { get; }
        public List<Person2> Members { get; }

        // Added direct tracking index to allow O(1) swap-to-last removals
        public int ListIndex { get; set; }

        public Household2(int id, params Person2[] persons)
        {
            Id = id;
            Members = new List<Person2>(persons);
        }
    }

    public struct HouseholdSizeEvent2
    {
        public Household2 TargetHousehold;
        public LifecycleType Type;
        public Person2 TargetPerson;

        public HouseholdSizeEvent2(Household2 household, LifecycleType type, Person2 targetPerson = null)
        {
            TargetHousehold = household;
            Type = type;
            TargetPerson = targetPerson;
        }
    }

    public class Simulation2
    {
        public List<Household2> Households { get; } = new List<Household2>(Globals.nPop * 10);
        private ConcurrentQueue<HouseholdSizeEvent2> _structuralEventQueue = new ConcurrentQueue<HouseholdSizeEvent2>();

        // Wrapped creation helper to assign the correct original list index
        public void CreateHousehold(int id, params Person2[] persons)
        {
            var hh = new Household2(id, persons);
            hh.ListIndex = Households.Count;
            Households.Add(hh);
        }

        public void RunYearlySimulationCycle()
        {
            Parallel.ForEach(Households, new ParallelOptions { MaxDegreeOfParallelism = Globals.nThreads }, hh =>
            {
                for (int i = 0; i < hh.Members.Count; i++)
                {
                    hh.Members[i].Age++;
                }

                int id = hh.Id;
                if (id % 10 == 0)
                {
                    _structuralEventQueue.Enqueue(new HouseholdSizeEvent2(hh, LifecycleType.Birth));
                }
                else if (id % 10 == 1)
                {
                    Person2 deadPerson = null;
                    for (int i = 0; i < hh.Members.Count; i++)
                    {
                        if (hh.Members[i].Age == 65)
                        {
                            deadPerson = hh.Members[i];
                            break;
                        }
                    }
                    _structuralEventQueue.Enqueue(new HouseholdSizeEvent2(hh, LifecycleType.Death, deadPerson));
                    _structuralEventQueue.Enqueue(new HouseholdSizeEvent2(hh, LifecycleType.Birth));
                }
                else if (id % 10 == 2)
                {
                    Person2 deadPerson = null;
                    for (int i = 0; i < hh.Members.Count; i++)
                    {
                        if (hh.Members[i].Age == 33)
                        {
                            deadPerson = hh.Members[i];
                            break;
                        }
                    }
                    _structuralEventQueue.Enqueue(new HouseholdSizeEvent2(hh, LifecycleType.Death, deadPerson));
                }
            });

            while (_structuralEventQueue.TryDequeue(out var ev))
            {
                var hh = ev.TargetHousehold;

                // If a household was already removed earlier in this exact queue sequence (due to multiple deaths)
                if (hh.ListIndex >= Households.Count || Households[hh.ListIndex] != hh)
                    continue;

                if (ev.Type == LifecycleType.Birth)
                {
                    if (hh.Members.Count >= Globals.maxSizeHusholdning)
                        throw new InvalidOperationException("Household size exceeded maximum limit.");

                    hh.Members.Add(new Person2(0, EHf.Ingen));
                }
                else if (ev.Type == LifecycleType.Death)
                {
                    if (ev.TargetPerson != null)
                    {
                        hh.Members.Remove(ev.TargetPerson);
                    }
                    else if (hh.Members.Count > 0)
                    {
                        hh.Members.RemoveAt(hh.Members.Count - 1);
                    }

                    if (hh.Members.Count == 0)
                    {
                        int targetIdx = hh.ListIndex;
                        int lastIdx = Households.Count - 1;

                        if (targetIdx != lastIdx)
                        {
                            // Swap: Place the last household into the dead household's position
                            var lastHousehold = Households[lastIdx];
                            Households[targetIdx] = lastHousehold;
                            lastHousehold.ListIndex = targetIdx; // Update its position pointer
                        }

                        // Instant O(1) removal from the end of the array
                        Households.RemoveAt(lastIdx);
                        hh.ListIndex = -1; // Mark as completely dead
                    }
                }
            }
        }
    }
}