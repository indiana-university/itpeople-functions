#!/bin/bash
for i in $(find . -name '*.fs')  # or whatever file extensions...
do
  if ! grep -q Copyright $i
  then
    cat copyright.txt $i >$i.new && mv $i.new $i
  fi
done