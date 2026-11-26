// Generates an ESP plugin file which overrides the HasWater flag to false for all CELLs
unit _mappalachia_noWater;

	uses _mappalachia_lib;

	const
		patchName = 'mappalachia_noWater.esp';
	var
		patchFile : IInterface;

	procedure Initialize;
	var
		i : Integer;
	begin
		patchFile := AddNewFileName(patchName);

		for i := 0 to FileCount() - 1 do begin
			esmIndex := i;
			targetESM := FileByIndex(esmIndex);
			fileName := GetFileName(targetESM);

			if (GetIsEsm(targetESM) = false) then begin
				AddMessage('Skipping ' + fileName + ' - not an ESM');
				continue;
			end;

			AddMessage('Adding to noWater patch: ' + fileName);

			findCells(GroupBySignature(targetESM, 'CELL'));
			findCells(GroupBySignature(targetESM, 'WRLD'));
		end;

		CleanMasters(patchFile);
		AddMessage('Water patch generation complete: ' + patchName);
	end;

	procedure findCells(item : IInterface);
	var
		i : Integer;
	begin
		if (GroupType(item) = 6) then exit; // Skip 'child' groups

		if (GetLoadOrderFormID(item) = 0) then begin // This is a GRUP and not an end-node, so pass each of its children back through
			for i := 0 to ElementCount(item) - 1 do begin
				findCells(ElementByIndex(item, i)); // Recursive
			end;
		end
		else if (Signature(item) = 'CELL') then begin // This is an end-node
			patchCell(item);
		end;
	end;

	procedure patchCell(cell : IInterface);
	var
		patchedCell : IInterface;
	begin
		if (IsWinningOverride(cell)) and (GetEditValue(ElementByName(ElementBySignature(cell, 'DATA'), 'Has Water')) = '1') then begin
			AddMasterIfMissing(patchFile, GetFileName(GetFile(MasterOrSelf(cell))));
			AddMasterIfMissing(patchFile, fileName);

			patchedCell := wbCopyElementToFile(cell, patchFile, False, True);
			SetEditValue(ElementByName(ElementBySignature(patchedCell, 'DATA'), 'Has Water'), '0');
		end;
	end;
end.