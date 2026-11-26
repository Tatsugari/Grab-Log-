-- Required for "only borrower can return" rule
alter table tools add column if not exists borrowed_card_uid text;
