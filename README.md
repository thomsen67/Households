Forsøg på at repræsentere en mikrosimulationsmodel med Households og Persons mere "kolonnebaseret" datamæssigt, hvor hver Household eller Person egl. bare bliver et "view" af 
nogle underliggende (store) arrays. Der har været forsøgt med 1 array for husholdninger med 1 person, 1 array for husholdinger med 2 personer osv. for at undgå RAM-fragmentering ("buckets"). Det viser sig dog at køre hurtigere at sige, at vi har har 1 array til det hele (for hver egenskab, f.eks. alder). Husholdningerne får så hver deres
plads ("slot") i disse arrays, med f.eks. plads til 5 personer. Hvis en husholdning så har > 5 personer, gives den to "slots" (med plads til 10 personer) osv.

Det er stadig tanken, at Household og Person skal ligne objekter, når man kigger på dem i Visual Studios debugger. Altså at man kan se deres felter, at man kan se
en liste over Persons i en Household, og at Person kan vise hvilken Household den tilhører. Men den underliggende "backend" er ikke objekter, men (store) arrays.

I eksemplet er der få "new" keywords, og "new Household..." eller "new Person..." danner ikke et objekt, men en struct (som allokeres i stacken og ikke i heapen,
hvilket kører en del hurtigere).

Der køres nogle hastighedstests, hvor Program2.cs er objektorienteret, Program.cs er "buckets", og Program3.cs er "slots". Program.cs og Program3.cs er begge ca. 10x hurtigere til indlæsning af husholdninger end Program2.cs. Derimod kører Program3.cs ca. 20x hurtigere end Program2.cs mht. simulationer, mens Program.cs "kun" er dobbelt så hurtig som Program2.cs.
