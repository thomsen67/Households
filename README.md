Forsøg på at repræsentere en mikrosimulationsmodel med Households og Persons mere "kolonnebaseret" datamæssigt, hvor hver Household eller Person egl. bare bliver et "view" af 
nogle underliggende (store) arrays. Der vil så være 1 array for husholdninger med 1 person, 1 array for husholdinger med 2 personer osv. 
Dette er for at undgå fragmentering når
husholdningers størrelse ændrer sig og husholdninger derfor flyttes mellem disse arrays. Der er så noget logik mht. dette, f.eks. at hvis en 2-personers husholdning fjernes,
så flyttes den sidste 2-personers husholdning ind på den tomme plads.

Det er stadig tanken, at Household og Person skal ligne objekter, når man kigger på dem i Visual Studios debugger. Altså at man kan se deres felter, at man kan se
en liste over Persons i en Household, og at Person kan vise hvilken Household den tilhører. Men den underliggende "backend" er ikke objekter, men (store) arrays.

I eksemplet er der få "new" keywords, og "new Household..." eller "new Person..." danner ikke et objekt, men en struct (som allokeres i stacken og ikke i heapen,
hvilket kører en del hurtigere).

Det er tanken at lave et tilsvarende eksempel, hvor Households og Persons er almindelige objekter, og hvor Persons er tilknyttet Households som LinkedLists.
Og derefter sammenligne hastighed.

